"""Build an allowlisted portable test kit. Never copy a user's data/profile/model cache."""
import argparse
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
parser=argparse.ArgumentParser()
parser.add_argument('--architecture', choices=['x86','x64'], default='x64')
args=parser.parse_args()
ARCH=args.architecture
PLATFORM=ROOT/'artifacts/platforms'/ARCH
BUILD = ROOT / ('artifacts/build/AgitCon-'+ARCH)
VERSION = json.loads((ROOT/'version.json').read_text('utf-8-sig'))['version']
OUT = ROOT / 'artifacts/packages' / ('AgitCon-release-' + ARCH + '-' + VERSION + '-' + uuid.uuid4().hex[:8])
OUT.mkdir(parents=True, exist_ok=False)


def copy(source, relative):
    target = OUT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


def write(relative, text):
    target = OUT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding='utf-8' if str(relative).endswith(('._pth', '.json')) else 'utf-8-sig')


copy(BUILD / ('AgitCon-'+ARCH+'.exe'), 'AgitCon.exe')
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
copy(ROOT / 'tools/import-portable.py', 'codecs/import-portable.py')

# Share exactly one embedded Python runtime between sender and autoplay.
for file in sorted((PLATFORM/'python').iterdir()):
    if file.is_file(): copy(file, Path('runtime/python')/file.name)
copy(PLATFORM/'python/LICENSE.txt','licenses/Python.txt')
AUTOPLAY_FILES = ['kakao-portable-launcher.py','kakao_update.py','kakao_diagnostics.py',
    'kakao-autoplay-service.py','trace-kakao-player.py','kakao-bitmap-bridge.js','kakao-auto-bridge.js',
    'kakao-original-download.js','kakao_qt_worker.py','kakao-qt-live.js','kakao-qt-photo.qml',
    'kakao-gif-discovery.py','kakao-webp-frames.py','kakao_download_startup.py','kakao_lifetime.py',
    'kakao_install.py','kakao-compact-media.js','kakao-photo-hover.js','KAKAO-GIF-EXPERIMENT.md']
for name in AUTOPLAY_FILES: copy(ROOT/'autoplay/tools'/name, Path('codecs/autoplay/tools')/name)
copy(ROOT/'autoplay/UPSTREAM.md','licenses/Autoplay-upstream.md')
copy(ROOT/'version.json','codecs/autoplay/version.json')
for directory in ['PIL', 'imageio_ffmpeg','frida']:
    source = PLATFORM / 'deps' / directory
    for file in sorted(source.rglob('*')):
        if file.is_file() and '__pycache__' not in file.parts and file.suffix != '.pyc':
            copy(file, Path('codecs/deps') / directory / file.relative_to(source))
for directory in ['pillow-13.0.0.dist-info', 'imageio_ffmpeg-0.8.1.dist-info','frida-17.27.4.dist-info']:
    source = PLATFORM / 'deps' / directory
    copy(source / 'METADATA', Path('licenses') / (directory + '.txt'))
    for file in sorted((source / 'licenses').rglob('*')):
        if file.is_file():
            copy(file, Path('licenses') / directory / file.relative_to(source / 'licenses'))
license_cache = ROOT / 'third_party/GPL-3.0.txt'
copy(license_cache, 'licenses/FFmpeg-GPL-3.0.txt')
ffmpeg = next((OUT / 'codecs/deps/imageio_ffmpeg/binaries').glob('ffmpeg*.exe'))
version = subprocess.run([str(ffmpeg), '-version'], capture_output=True, text=True, check=True).stdout
import re
ffmpeg_version=re.search(r'ffmpeg version ([0-9]+\.[0-9]+(?:\.[0-9]+)?)',version).group(1)
write('licenses/FFmpeg-build.txt', version + '\nUnmodified executable distributed separately from AgitCon.\nBinary distributor: https://github.com/imageio/imageio-ffmpeg\nUpstream source: https://github.com/FFmpeg/FFmpeg/tree/n' + ffmpeg_version + '\nFFmpeg and linked-library build/source information: https://www.gyan.dev/ffmpeg/builds/#about-these-builds\n')

def machine(path):
    import struct
    with path.open('rb') as stream:
        assert stream.read(2)==b'MZ', path
        stream.seek(60); offset=struct.unpack('<I',stream.read(4))[0]
        stream.seek(offset); assert stream.read(4)==b'PE\0\0'
        return struct.unpack('<H',stream.read(2))[0]
expected=0x8664 if ARCH=='x64' else 0x14c
for file in [OUT/'AgitCon.exe',OUT/'AgitCon.Updater.exe',OUT/'WebView2Loader.dll',OUT/'runtime/python/python.exe',OUT/'runtime/python/python311.dll',*list((OUT/'codecs/deps/PIL').glob('*.pyd')),*list((OUT/'codecs/deps/frida').glob('*.pyd')),ffmpeg]:
    assert machine(file)==expected, ('Native architecture mismatch', file, ARCH)
write('README.txt', "AgitCon / KakaoTalk emoji sender " + VERSION + "\nhttps://github.com/Ch0m1n/KakaoTalk-emoji-sender\nExtract the complete ZIP into a writable folder and run AgitCon.exe.\nSender and autoplay share one app, tray and update. Settings > Autoplay opens playback controls. Stop any standalone KakaoAutoPlay first.\nSettings > Update: check releases and install updates.\nUser settings, packs and models are not included in this ZIP.\n")
files = sorted(p for p in OUT.rglob('*') if p.is_file())
inventory = [{'path': p.relative_to(OUT).as_posix(), 'bytes': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in files]
write('package-manifest.json', json.dumps({'version': VERSION, 'architecture': ARCH, 'autoplayUpstream': 'v11.3.0@387eb04', 'files': inventory}, indent=2))
release = ROOT/'releases'/VERSION
release.mkdir(parents=True, exist_ok=True)
archive = release/('AgitCon-win-'+ARCH+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as target:
    for file in sorted(OUT.rglob('*')):
        if file.is_file(): target.write(file,file.relative_to(OUT).as_posix())
digest=hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix('.zip.sha256').write_text(digest+'  '+archive.name+'\n',encoding='ascii')
print(json.dumps({'zip':str(archive),'bytes':archive.stat().st_size,'sha256':digest,'files':len(inventory)+1}))

(ROOT/'artifacts/packages'/('latest-'+ARCH+'.json')).write_text(json.dumps({'stage':str(OUT),'zip':str(archive),'sha256':digest}),encoding='utf-8')
