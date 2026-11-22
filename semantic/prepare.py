"""Explicit-only installer for local EmbeddingGemma 2 q4; no pip or PyTorch."""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import time
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent
APP = ROOT.parent
WEB = ROOT / 'gemma2' / 'web'
SPEC = 'gemma2-q4-image280-gif3-v1'
REVISION = 'daa72c51243991dfcaf9f9137d2c573d8f7790c0'
SDK = '1.0.4314.35'
ORT = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.31.0-dev.20260914-8d85527a0/dist/'
RUNTIME = [
    ('https://cdn.jsdelivr.net/npm/@huggingface/transformers@4.3.1/dist/transformers.min.js', 'transformers.min.js', '8d6716d9086f57c30a4bf367dba61b887593573c770c454465e8019b2703e743'),
    (ORT + 'ort-wasm-simd-threaded.asyncify.mjs', 'ort/ort-wasm-simd-threaded.asyncify.mjs', '0966b6105cd936744498aa60df7a22cbd47af3374dbc64a9ab561c08a71e3611'),
    (ORT + 'ort-wasm-simd-threaded.asyncify.wasm', 'ort/ort-wasm-simd-threaded.asyncify.wasm', '49871f5a4409519797e127440868a6d1923339d9185907f301a5b2a1d90af082'),
]
MODEL_FILES = ['config.json', 'preprocessor_config.json', 'processor_config.json', 'tokenizer.json', 'tokenizer_config.json',
               'onnx/model_q4.onnx', 'onnx/model_q4.onnx_data', 'onnx/vision_encoder_q4.onnx', 'onnx/vision_encoder_q4.onnx_data']

class Progress:
    def __init__(self, files, sizes, clock=time.monotonic, sink=None):
        self.sizes = {str(path): sizes.get(Path(path).name, 0) for path in files}
        self.done = {key: 0 for key in self.sizes}
        self.completed = set()
        self.clock = clock
        self.started = self.clock()
        self.last = -1.0
        self.transferred = 0
        self.sink = sink or (lambda data: print(json.dumps(data, ensure_ascii=True), flush=True))

    def update(self, target, current, size=0, downloaded=0, complete=False, phase='download', force=False):
        key = str(target)
        if size:
            self.sizes[key] = size
        self.done[key] = current
        self.transferred += downloaded
        if complete:
            self.completed.add(key)
        now = self.clock()
        if not force and now - self.last < .2:
            return
        self.last = now
        total, done = sum(self.sizes.values()), sum(self.done.values())
        known = all(self.sizes.values())
        elapsed = now - self.started
        rate = self.transferred / elapsed if elapsed >= .5 and self.transferred >= 262144 else 0
        remaining = max(0, total - done)
        self.sink({'type': 'progress', 'phase': phase, 'file': Path(target).name,
                   'completed': len(self.completed), 'count': len(self.sizes),
                   'done': done, 'total': total if known else 0, 'remaining': remaining if known else -1,
                   'percent': min(99, int(done * 100 / total)) if known and total else -1,
                   'rate': rate, 'eta': math.ceil(remaining / rate) if known and rate and remaining else -1})

    def phase(self, phase):
        self.sink({'type': 'progress', 'phase': phase, 'percent': 100 if phase == 'done' else 99,
                   'completed': len(self.completed), 'count': len(self.sizes),
                   'done': sum(self.done.values()), 'total': sum(self.sizes.values()),
                   'remaining': 0, 'rate': 0, 'eta': -1, 'file': ''})

def integrity():
    data = json.loads((ROOT / 'download-integrity.json').read_text(encoding='utf-8'))
    if data['spec'] != SPEC or data['revision'] != REVISION or data['sdk'] != SDK:
        raise ValueError('Integrity manifest does not match pinned model version')
    return {Path(item['path']).name: item for item in data['files']}

def fetch(url, target, expected=None, progress=None):
    target = Path(target)
    pinned = integrity().get(target.name)
    expected = expected or (pinned['sha256'] if pinned else None)
    pinned_size = pinned['bytes'] if pinned else 0
    if target.is_file() and target.stat().st_size and (not pinned_size or target.stat().st_size == pinned_size) and (expected is None or hashlib.sha256(target.read_bytes()).hexdigest() == expected):
        if progress:
            progress.update(target, target.stat().st_size, size=target.stat().st_size, complete=True, phase='cached', force=True)
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + '.part')
    if not progress:
        print('Downloading ' + target.name, flush=True)
    else:
        progress.update(target, 0, force=True)
    request = urllib.request.Request(url, headers={'User-Agent': 'AgitCon/1.0 local model installer'})
    digest, total = hashlib.sha256(), 0
    with urllib.request.urlopen(request, timeout=90) as response, temporary.open('wb') as output:
        expected_size = int(response.headers.get('Content-Length', 0))
        if progress:
            progress.update(target, 0, size=expected_size, force=True)
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            total += len(chunk)
            if total > 512 * 1024 * 1024:
                raise ValueError('Asset exceeds size limit')
            digest.update(chunk)
            output.write(chunk)
            if progress:
                progress.update(target, total, size=expected_size, downloaded=len(chunk))
    if not total or (pinned_size and total != pinned_size) or (expected_size and total != expected_size) or (expected and digest.hexdigest() != expected):
        raise ValueError('Incomplete download or integrity check failed: ' + target.name)
    temporary.replace(target)
    if progress:
        progress.update(target, total, size=total, complete=True, force=True)

def sdk(progress=None):
    package = ROOT / 'gemma2' / ('webview2-' + SDK + '.nupkg')
    fetch('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/' + SDK + '/microsoft.web.webview2.' + SDK + '.nupkg', package, progress=progress)
    with zipfile.ZipFile(package) as archive:
        for source, destination in [('lib/net462/Microsoft.Web.WebView2.Core.dll', APP / 'Microsoft.Web.WebView2.Core.dll'),
                                    ('runtimes/win-x64/native/WebView2Loader.dll', APP / 'WebView2Loader.dll'),
                                    ('LICENSE.txt', ROOT / 'gemma2' / 'WEBVIEW2-LICENSE.txt')]:
            if source not in archive.namelist() and source == 'LICENSE.txt':
                continue
            contents = archive.read(source)
            # Reinstalling the same version must not overwrite a loaded DLL.
            if not destination.is_file() or destination.read_bytes() != contents:
                destination.write_bytes(contents)

def prepare():
    files = [ROOT / 'gemma2' / ('webview2-' + SDK + '.nupkg')]
    files += [WEB / relative for _, relative, _ in RUNTIME]
    files += [WEB / 'models' / 'gemma2' / relative for relative in MODEL_FILES]
    files += [ROOT / 'gemma2' / 'MODEL-CARD.md']
    pinned = integrity()
    if any(path.name not in pinned for path in files):
        raise ValueError('Every install asset must have a pinned size and hash')
    sizes_path = ROOT / 'download-sizes.json'
    sizes = json.loads(sizes_path.read_text(encoding='utf-8')) if sizes_path.is_file() else {}
    progress = Progress(files, sizes)
    sdk(progress)
    for url, relative, digest in RUNTIME:
        fetch(url, WEB / relative, digest, progress)
    base = 'https://huggingface.co/onnx-community/embeddinggemma-2-ONNX/resolve/' + REVISION + '/'
    for relative in MODEL_FILES:
        fetch(base + relative, WEB / 'models' / 'gemma2' / relative, progress=progress)
    fetch(base + 'README.md', ROOT / 'gemma2' / 'MODEL-CARD.md', progress=progress)
    progress.phase('verify')
    manifest = {'spec': SPEC, 'model': 'onnx-community/embeddinggemma-2-ONNX', 'revision': REVISION, 'dtype': 'q4', 'device': 'webgpu',
                'files': {str(path.relative_to(WEB)): {'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
                          for path in WEB.rglob('*') if path.is_file() and path.suffix != '.part'}}
    (ROOT / 'gemma2' / 'assets.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    (ROOT / 'gemma2' / 'ready.txt').write_text(SPEC, encoding='utf-8')
    progress.phase('done')

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--sdk-only', action='store_true')
    args = parser.parse_args()
    sdk() if args.sdk_only else prepare()
