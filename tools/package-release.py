"""Build an allowlisted portable test kit. Never copy a user's data/profile/model cache."""
import hashlib
import json
import shutil
import subprocess
import sys
import urllib.request
import zipfile
import uuid
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / 'dist'
BUILD = ROOT / 'artifacts/build/AgitCon-next'
VERSION = json.loads((ROOT/'version.json').read_text('utf-8-sig'))['version']
OUT = ROOT / 'artifacts/packages' / ('AgitCon-release-' + VERSION + '-' + uuid.uuid4().hex[:8])
OUT.mkdir(parents=True, exist_ok=False)


def copy(source, relative):
    target = OUT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


def write(relative, text):
    target = OUT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding='utf-8' if str(relative).endswith(('._pth', '.json')) else 'utf-8-sig')


copy(BUILD / 'AgitCon-next.exe', 'AgitCon.exe')
for name in ['AgitCon.Updater.exe','version.json','AgitCon.ico', 'Microsoft.Web.WebView2.Core.dll', 'WebView2Loader.dll']:
    copy(BUILD / name, name)
manifest = json.loads((BUILD / 'semantic/build.json').read_text('utf-8-sig'))
assert hashlib.sha256((OUT / 'AgitCon.exe').read_bytes()).hexdigest().upper() == manifest['buildHash'].upper()
for item in manifest['files']:
    path = Path(item['path'])
    assert not path.is_absolute() and '..' not in path.parts
    source = BUILD / 'semantic' / path
    assert hashlib.sha256(source.read_bytes()).hexdigest().upper() == item['sha256'].upper()
    copy(source, Path('semantic') / path)
copy(ROOT / 'semantic/THIRD_PARTY.md', 'licenses/semantic-components.md')
copy(DIST / 'semantic/gemma2/WEBVIEW2-LICENSE.txt', 'licenses/WebView2.txt')
copy(ROOT / 'codecs/convert.py', 'codecs/convert.py')

# A private, isolated Python runtime built only from the standard installation.
# Exclude all globally installed packages, caches, tests and host path markers.
base = Path(sys.base_prefix)
assert sys.version_info[:2] == (3, 11)
runtime = OUT / 'runtime/python'
runtime.mkdir(parents=True)
for name in ['python.exe', 'python3.dll', 'python311.dll', 'vcruntime140.dll', 'vcruntime140_1.dll']:
    copy(base / name, Path('runtime/python') / name)
copy(base / 'LICENSE.txt', 'licenses/Python.txt')
for file in sorted((base / 'DLLs').iterdir()):
    if file.suffix.lower() in ('.pyd', '.dll'):
        copy(file, Path('runtime/python/DLLs') / file.name)
with zipfile.ZipFile(runtime / 'python311.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for file in sorted((base / 'Lib').rglob('*.py')):
        relative = file.relative_to(base / 'Lib')
        if any(p in {'site-packages', '__pycache__', 'test', 'tests', 'idlelib', 'tkinter', 'turtledemo', 'ensurepip'} for p in relative.parts):
            continue
        archive.write(file, relative.as_posix())
write('runtime/python/python311._pth', 'python311.zip\n.\nDLLs\n../../codecs/deps\n../../semantic\n')
for directory in ['PIL', 'imageio_ffmpeg']:
    source = DIST / 'codecs/deps' / directory
    for file in sorted(source.rglob('*')):
        if file.is_file() and '__pycache__' not in file.parts and file.suffix != '.pyc':
            copy(file, Path('codecs/deps') / directory / file.relative_to(source))
for directory in ['pillow-13.0.0.dist-info', 'imageio_ffmpeg-0.8.1.dist-info']:
    source = DIST / 'codecs/deps' / directory
    copy(source / 'METADATA', Path('licenses') / (directory + '.txt'))
    for file in sorted((source / 'licenses').rglob('*')):
        if file.is_file():
            copy(file, Path('licenses') / directory / file.relative_to(source / 'licenses'))
license_cache = ROOT / 'third_party/GPL-3.0.txt'
copy(license_cache, 'licenses/FFmpeg-GPL-3.0.txt')
ffmpeg = next((OUT / 'codecs/deps/imageio_ffmpeg/binaries').glob('ffmpeg*.exe'))
version = subprocess.run([str(ffmpeg), '-version'], capture_output=True, text=True, check=True).stdout
write('licenses/FFmpeg-build.txt', version + '\nUnmodified executable distributed separately from AgitCon.\nBinary provider: https://www.gyan.dev/ffmpeg/builds/\nUpstream source: https://github.com/FFmpeg/FFmpeg/tree/n9.0.2\nFFmpeg and linked-library build/source information: https://www.gyan.dev/ffmpeg/builds/#about-these-builds\n')

write('README.txt', "AgitCon / KakaoTalk emoji sender " + VERSION + "\nhttps://github.com/Ch0m1n/KakaoTalk-emoji-sender\nExtract the complete ZIP into a writable folder and run AgitCon.exe.\nSettings > Update: check releases and install updates.\nUser settings, packs and models are not included in this ZIP.\n")
files = sorted(p for p in OUT.rglob('*') if p.is_file())
inventory = [{'path': p.relative_to(OUT).as_posix(), 'bytes': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in files]
write('package-manifest.json', json.dumps({'version': VERSION, 'files': inventory}, indent=2))
release = ROOT/'releases'/VERSION
release.mkdir(parents=True, exist_ok=True)
archive = release/'AgitCon-win-x64.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as target:
    for file in sorted(OUT.rglob('*')):
        if file.is_file(): target.write(file,file.relative_to(OUT).as_posix())
digest=hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix('.zip.sha256').write_text(digest+'  '+archive.name+'\n',encoding='ascii')
print(json.dumps({'zip':str(archive),'bytes':archive.stat().st_size,'sha256':digest,'files':len(inventory)+1}))
