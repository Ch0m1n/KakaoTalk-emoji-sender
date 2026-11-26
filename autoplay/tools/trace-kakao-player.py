"""Bounded, temporary instrumentation of known image functions in one Kakao version."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import time
import uuid
from collections import deque

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent / 'deps'))
sys.path.insert(0, str(ROOT / 'tools'))
import frida
from kakao_download_startup import start_downloader
from kakao_lifetime import owner_alive, manager_alive
from kakao_install import supported_process, machine

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--pid', type=int, required=True)
parser.add_argument('--seconds', type=int, default=20)
parser.add_argument('--bridge', action='store_true', help='Run the guarded experimental bitmap bridge instead of the tracer')
parser.add_argument('--chat-window', type=int, help='Verified chat HWND required for bridge mode')
parser.add_argument('--stop-file', type=Path, help='Stop and detach when this file exists')
parser.add_argument('--source-gif', type=Path, help='Load a specific local GIF without opening the viewer')
parser.add_argument('--auto-gifs', action='store_true', help='Automatically discover and play multiple local GIFs')
parser.add_argument('--download-originals', action='store_true', help='Acquire originals for newly constructed GIF/WebP thumbnails')
parser.add_argument('--control-file', type=Path, help='Supervisor window list; enables persistent bridge lifetime')
parser.add_argument('--watch-dir', action='append', type=Path, default=[])
parser.add_argument('--catalog', type=Path, default=ROOT / 'dist/data/catalog.json')
parser.add_argument('--no-default-sources', action='store_true', help='Use only explicit watched directories (for isolated tests)')
args = parser.parse_args()
if not (args.seconds == 0 and args.bridge and args.auto_gifs and args.control_file) and not 1 <= args.seconds <= (1800 if args.bridge else 60):
    raise ValueError('Duration exceeds bounded session limit')
if args.bridge and not args.chat_window:
    parser.error('--bridge requires --chat-window')
if args.auto_gifs and (not args.bridge or args.source_gif):
    parser.error('--auto-gifs requires --bridge and cannot combine with --source-gif')
if args.download_originals and not args.auto_gifs:
    parser.error('--download-originals requires --auto-gifs')
if args.stop_file and args.stop_file.exists():
    parser.error('Stop file already exists; choose a fresh stop-file path')
if args.source_gif:
    if not args.bridge:
        parser.error('--source-gif requires --bridge')
    args.source_gif = args.source_gif.resolve(strict=True)
    if args.source_gif.stat().st_size > 32*1024*1024:
        parser.error('Source GIF exceeds 32 MiB input limit')
    with args.source_gif.open('rb') as gif:
        if gif.read(6) not in (b'GIF87a', b'GIF89a'):
            parser.error('Source is not a GIF file')
exe=supported_process(args.pid)
if exe is None:raise RuntimeError('실행 중인 카톡 경로를 확인하지 못했어요.')
if exe.name.casefold()=='kakaotalkui.exe':
    from kakao_qt_worker import run
    raise SystemExit(run(args))
target_arch='x64' if machine(exe)==0x8664 else 'ia32'
if not args.bridge:raise RuntimeError('고정 내부 주소 추적 모드는 일반 배포에서 사용하지 않아요.')

source = r'''
const module = Process.getModuleByName('KakaoTalk.exe');
if (Process.arch !== 'ia32') throw Error('Expected x86');
const targets = [
  ['gif_draw', 0xe33c30], ['webp_draw', 0xc3be30],
  ['gif_start', 0xe33d00], ['webp_start', 0xc3bea0],
  ['thumbnail_refresh', 0xe601f0]
];
const counts = {};
const stacks = {};
const images = {};
const listeners = [];
try {
  for (const [name, rva] of targets) {
    const address = module.base.add(rva);
    const first = address.readU8();
    if (first !== 0x55 && first !== 0x53 && first !== 0x56) throw Error('Unexpected entry bytes: ' + name);
    listeners.push(Interceptor.attach(address, {
      onEnter() {
        const key = name + ':' + this.threadId + ':' + this.returnAddress.sub(module.base);
        counts[key] = (counts[key] || 0) + 1;
      }
    }));
  }
  for (const name of ['BitBlt', 'StretchBlt']) {
    const address = Process.getModuleByName('gdi32.dll').findExportByName(name);
    if (address !== null) listeners.push(Interceptor.attach(address, {
      onEnter() {
        const key = name + ':' + this.threadId + ':' + this.returnAddress.sub(module.base);
        counts[key] = (counts[key] || 0) + 1;
        if (stacks[key] === undefined && Object.keys(stacks).length < 40) {
          stacks[key] = Thread.backtrace(this.context, Backtracer.ACCURATE)
            .slice(0, 18).map(address => {
              const owner = Process.findModuleByAddress(address);
              return owner ? owner.name + '+' + address.sub(owner.base) : 'unknown';
            });
        }
      }
    }));
  }
  const gdiplus = Process.findModuleByName('gdiplus.dll');
  if (gdiplus !== null) {
    const frameCount = new NativeFunction(gdiplus.getExportByName('GdipImageGetFrameCount'), 'int', ['pointer', 'pointer', 'pointer'], 'stdcall');
    const dimensionsCount = new NativeFunction(gdiplus.getExportByName('GdipImageGetFrameDimensionsCount'), 'int', ['pointer', 'pointer'], 'stdcall');
    const dimensionsList = new NativeFunction(gdiplus.getExportByName('GdipImageGetFrameDimensionsList'), 'int', ['pointer', 'pointer', 'uint'], 'stdcall');
    const width = new NativeFunction(gdiplus.getExportByName('GdipGetImageWidth'), 'int', ['pointer', 'pointer'], 'stdcall');
    const height = new NativeFunction(gdiplus.getExportByName('GdipGetImageHeight'), 'int', ['pointer', 'pointer'], 'stdcall');
    for (const name of ['GdipImageSelectActiveFrame', 'GdipDrawImageRectRectI', 'GdipDrawImageRectI', 'GdipDrawImageRectRect', 'GdipDrawImageRect', 'GdipDrawImageI', 'GdipDrawImage', 'GdipCreateHBITMAPFromBitmap']) {
      const address = gdiplus.findExportByName(name);
      if (address === null) continue;
      listeners.push(Interceptor.attach(address, {
        onEnter(args) {
          const key = name + ':' + this.threadId + ':' + this.returnAddress.sub(module.base);
          counts[key] = (counts[key] || 0) + 1;
          const img = args[(name === 'GdipImageSelectActiveFrame' || name === 'GdipCreateHBITMAPFromBitmap') ? 0 : 1];
          const imageKey = img.toString();
          if (!images[imageKey] && Object.keys(images).length < 120) {
            const n = Memory.alloc(4), w = Memory.alloc(4), h = Memory.alloc(4);
            let status = dimensionsCount(img, n);
            const dimensionTotal = status === 0 ? n.readU32() : 0;
            if (dimensionTotal > 0 && dimensionTotal <= 8) {
              const dimensions = Memory.alloc(dimensionTotal * 16);
              status = dimensionsList(img, dimensions, dimensionTotal);
              if (status === 0) status = frameCount(img, dimensions, n);
            } else status = -1;
            width(img,w); height(img,h);
            images[imageKey] = {frames:status === 0 ? n.readU32() : null, status, width:w.readU32(), height:h.readU32(), callers:{}};
          }
          if (images[imageKey]) images[imageKey].callers[key] = (images[imageKey].callers[key] || 0) + 1;
          if (stacks[key] === undefined && Object.keys(stacks).length < 60) {
            stacks[key] = Thread.backtrace(this.context, Backtracer.ACCURATE)
              .slice(0, 18).map(address => {
                const owner = Process.findModuleByAddress(address);
                return owner ? owner.name + '+' + address.sub(owner.base) : 'unknown';
              });
          }
        }
      }));
    }
  }
} catch (error) {
  for (const listener of listeners) listener.detach();
  throw error;
}
rpc.exports = { snapshot() { return {counts, stacks, images}; }, stop() { for (const l of listeners) l.detach(); } };
send({status:'attached', targets:targets.map(t=>t[0])});
'''
session = None
if args.bridge:
    source = 'const BRIDGE_CONFIG = ' + json.dumps({'hwnd': args.chat_window, 'managed': bool(args.control_file), 'sourceFile': str(args.source_gif) if args.source_gif else None}) + ';\n' + (ROOT / 'tools/kakao-bitmap-bridge.js').read_text(encoding='utf-8')
    if args.auto_gifs:
        source = source.split('const hwnd=')[0] + '\n'.join(
            (ROOT / 'tools' / name).read_text(encoding='utf-8')
            for name in ('kakao-compact-media.js', 'kakao-photo-hover.js', 'kakao-auto-bridge.js')
        )

def read_photo_filter():
    settings = Path(os.environ['LOCALAPPDATA']) / 'KakaoAutoPlay/settings.json'
    try:
        values = json.loads(settings.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return ('', '', False)
    if not isinstance(values, dict):
        return ('', '', False)
    room, sender = (values.get(key) for key in ('photoFilterRoom', 'photoFilterSender'))
    if all(isinstance(value, str) and 0 < len(value) < 256 for value in (room, sender)):
        return (room, sender, values.get('photoFilterHide', False) is True)
    return ('', '', False)
discovery = None
if args.auto_gifs:
    import importlib.util
    spec = importlib.util.spec_from_file_location('gif_discovery', ROOT / 'tools/kakao-gif-discovery.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    directories = list(args.watch_dir)
    if args.download_originals:
        originals_directory = ROOT / 'artifacts/kakao-internals/received-originals'
        originals_directory.mkdir(exist_ok=True)
        directories.append(originals_directory)
    if not args.no_default_sources:
        directories += [Path.home() / 'Documents/카카오톡 받은 파일', Path.home() / 'Downloads', ROOT / 'dist/data/images']
    discovery = module.GifDiscovery(None if args.no_default_sources else args.catalog, directories)
script = None
download_script = None
report = {'status': 'starting', 'pid': args.pid, 'architecture': target_arch, 'events': []}
if args.download_originals and target_arch=='x64':
    report['originalDownloadsUnavailable']='x64-original-download-not-implemented'
completed_originals = deque(maxlen=5000)
def on_message(message, data):
    original = message.get('payload', {}).get('originalFile') if message.get('type') == 'send' else None
    if original:
        completed_originals.append(original)
    report['events'].append(message)
    del report['events'][:-256]
    if not args.control_file or message.get('type') == 'error' or message.get('payload', {}).get('status'):
        print(json.dumps(message), flush=True)

try:
    if not owner_alive(ROOT/'artifacts/kakao-internals') or not manager_alive():
        raise InterruptedError('Playback owner exited')
    session = frida.attach(args.pid)
    script = session.create_script(source, runtime='v8')
    script.on('message', on_message)
    script.load()
    photo_filter = ('', '', False)
    if args.download_originals and target_arch=='ia32':
        import ctypes
        from ctypes import wintypes
        get_thread = ctypes.WinDLL('user32').GetWindowThreadProcessId
        get_thread.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        get_thread.restype = wintypes.DWORD
        owner = wintypes.DWORD()
        thread = get_thread(args.chat_window, ctypes.byref(owner))
        if not thread or owner.value != args.pid:
            raise RuntimeError('Original downloader rejected chat window owner')
        config = {'directory': str(originals_directory), 'session': uuid.uuid4().hex, 'thread': thread}
        download_source = 'const DOWNLOAD_CONFIG=' + json.dumps(config) + ';\n' + (ROOT / 'tools/kakao-original-download.js').read_text(encoding='utf-8')
        download_script = session.create_script(download_source)
        available, unavailable_reason = start_downloader(download_script, on_message)
        if not available:
            download_script = None
            report['originalDownloadsUnavailable'] = unavailable_reason
    media = script.exports_sync
    if discovery is not None:
        spec = importlib.util.spec_from_file_location('webp_frames', ROOT / 'tools/kakao-webp-frames.py')
        webp_module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(webp_module)
        media = webp_module.MediaSources(script.exports_sync, ROOT / 'artifacts/kakao-internals/webp-frames',cancelled=lambda:not owner_alive(ROOT/'artifacts/kakao-internals') or not manager_alive())
    if args.bridge:
        initial = script.exports_sync.snapshot()
        if initial['errors']:
            report['counts'] = initial
            raise RuntimeError('Bridge rejected source; see agent errors')
    deadline = time.monotonic() + args.seconds if args.seconds else float('inf')
    next_progress = time.monotonic() + 5
    while time.monotonic() < deadline:
        time.sleep(.5)
        if (args.stop_file and args.stop_file.exists()) or not owner_alive(ROOT/'artifacts/kakao-internals') or not manager_alive():
            break
        if args.control_file:
            try:
                control = json.loads(args.control_file.read_text(encoding='utf-8'))
                if control['pid'] != args.pid:
                    raise RuntimeError('Supervisor process identity changed')
                script.exports_sync.setwindows(control['windows'])
                if args.auto_gifs and target_arch == 'ia32':
                    selected_filter = read_photo_filter()
                    if selected_filter != photo_filter:
                        script.exports_sync.setphotofilter(*selected_filter)
                        photo_filter = selected_filter
            except (OSError, json.JSONDecodeError):
                pass  # A transient replacement/read error is retried next poll.
        if discovery is not None:
            while completed_originals:
                discovery.observe([completed_originals.popleft()], priority=True)
            work = script.exports_sync.work()
            discovery.observe(work['files'])
            for source_id in work['pending'][:8]:
                media.activate(source_id)
            report['discovery'] = discovery.poll(media)
        if args.bridge and time.monotonic() >= next_progress:
            next_progress = time.monotonic() + 5
            progress = script.exports_sync.snapshot()
            if download_script is not None:
                progress['originalDownloads'] = download_script.exports_sync.snapshot()
                progress['originalDownloads']['available'] = True
            elif report.get('originalDownloadsUnavailable'):
                progress['originalDownloads'] = {'available':False,'reason':report['originalDownloadsUnavailable']}
            (ROOT / 'artifacts/kakao-internals/bitmap-bridge-progress.json').write_text(
                json.dumps(progress, indent=2), encoding='utf-8')
            if progress.get('stoppedReason') or progress.get('errors'):
                break
    report['counts'] = script.exports_sync.snapshot()
    report['status'] = 'complete'
    if args.bridge and report['counts'].get('errors'):
        report['status'] = 'failed'
except (KeyboardInterrupt,InterruptedError):
    report['status'] = 'stopped'
    if script is not None:
        report['counts'] = script.exports_sync.snapshot()
except Exception as error:
    report['status'] = 'failed'
    report['error'] = str(error)
    print(str(error), flush=True)
finally:
    if download_script is not None:
        try:
            download_script.exports_sync.stop()
            # Keep redraw/dispatch alive until owned download objects are destroyed on the UI thread.
            while download_script.exports_sync.snapshot()['pending']:
                time.sleep(.25)
            report['originalDownloads'] = download_script.exports_sync.snapshot()
            download_script.exports_sync.detach()
            download_script.unload()
        except Exception as error:
            report['download_cleanup_error'] = str(error)
    if script is not None:
        try:
            if args.auto_gifs and target_arch == 'ia32':
                script.exports_sync.restorephotos()
                while script.exports_sync.snapshot().get('photoFilter', {}).get('restoring'):
                    time.sleep(.05)
            script.exports_sync.stop()
        except Exception as error:
            report['cleanup_error'] = str(error)
        try:
            script.unload()
        except Exception as error:
            report['unload_error'] = str(error)
    if session is not None:
        try:
            session.detach()
        except Exception as error:
            report['detach_error'] = str(error)
    directory = ROOT / 'artifacts/kakao-internals'
    prefix = 'bitmap-bridge' if args.bridge else 'player-live-trace'
    output = directory / (prefix + '-' + str(time.time_ns()) + '.json')
    if not args.control_file:
        output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    (directory / (prefix + '.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report), flush=True)
if report['status'] not in ('complete', 'stopped'):
    sys.exit(1)
