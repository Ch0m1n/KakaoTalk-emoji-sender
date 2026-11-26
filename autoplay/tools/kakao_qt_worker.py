"""Owned Qt photo replay worker; no chat text, media paths, or URLs are exported."""
import json
from pathlib import Path
import time
import frida
from kakao_lifetime import owner_alive, manager_alive

ROOT=Path(__file__).resolve().parents[1]
STATE=ROOT/'artifacts/kakao-internals'

def write(name,value):
    path=STATE/name;temporary=path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
    temporary.replace(path)

def run(args):
    if not args.bridge or not args.auto_gifs:raise ValueError('Qt requires managed photo replay')
    STATE.mkdir(parents=True,exist_ok=True)
    report={'status':'starting','engine':'qt6-live'};session=None;script=None
    try:
        if not owner_alive(STATE) or not manager_alive():raise InterruptedError('Owner exited')
        overlay=(ROOT/'tools/kakao-qt-photo.qml').read_text(encoding='utf-8')
        if not args.download_originals:overlay=overlay.replace('property bool autoDownload: true','property bool autoDownload: false')
        source='const OVERLAY_SOURCE='+json.dumps(overlay)+';\n'+(ROOT/'tools/kakao-qt-live.js').read_text(encoding='utf-8')
        session=frida.attach(args.pid);script=session.create_script(source,runtime='v8');script.load()
        if not script.exports_sync.setwindows([args.chat_window]):raise RuntimeError('Qt window owner changed')
        deadline=time.monotonic()+args.seconds if args.seconds else float('inf')
        while time.monotonic()<deadline and owner_alive(STATE) and manager_alive() and not (args.stop_file and args.stop_file.exists()):
            if args.control_file:
                try:
                    control=json.loads(args.control_file.read_text(encoding='utf-8'))
                    if control['pid']!=args.pid:raise RuntimeError('Supervisor process identity changed')
                    script.exports_sync.setwindows(control['windows'])
                except (OSError,json.JSONDecodeError):pass
            progress=script.exports_sync.snapshot()
            progress['architecture']='x64'
            progress['originalDownloads']={'available':True,'saved':progress.get('originalsReady',0),'mode':'native-qt-cache','concurrency':4,'pending':progress.get('downloadPending',0),'inFlight':progress.get('downloadInFlight',0)}
            write('bitmap-bridge-progress.json',progress)
            report['counts']=progress
            if progress['errors']:raise RuntimeError('Qt replay error; see diagnostic counters')
            time.sleep(.5)
        report['status']='stopped'
    except (KeyboardInterrupt,InterruptedError):report['status']='stopped'
    except Exception as error:
        report.update(status='failed',errorType=type(error).__name__,error=str(error))
    finally:
        if script:
            try:
                script.exports_sync.stop()
                until=time.monotonic()+5
                while time.monotonic()<until and not script.exports_sync.snapshot()['stopped']:time.sleep(.1)
                script.exports_sync.detach()
                report['cleanup']='scheduled-on-ui-thread'
                progress=script.exports_sync.snapshot()
                progress.update(architecture='x64',active=0,states=[],stopped=True)
                write('bitmap-bridge-progress.json',progress)
            except Exception as error:report['cleanupError']=type(error).__name__
            # If the GUI is blocked or the worker dies, replay items self-remove
            # after six seconds without a heartbeat once the GUI resumes.
            try:script.unload()
            except Exception:pass
        if session:
            try:session.detach()
            except Exception:pass
        write('bitmap-bridge.json',report)
    return 1 if report['status']=='failed' else 0
