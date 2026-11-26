"""Exercise packaged Python and owned native fixtures, never the KakaoTalk process."""
import argparse
import json
import subprocess
import shutil
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
(ROOT/'autoplay/artifacts/tests').mkdir(parents=True,exist_ok=True)
parser = argparse.ArgumentParser()
parser.add_argument('architecture', choices=['x86', 'x64'])
args = parser.parse_args()
arch = args.architecture
stage = Path(json.loads((ROOT/f'artifacts/packages/latest-{arch}.json').read_text('utf-8'))['stage'])
python = stage/'runtime/python/python.exe'


def run(command):
    result=subprocess.run([str(p) for p in command], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=180,
                   creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    print(result.stdout, end='');print(result.stderr, end='')
    result.check_returncode()


run([python, '-X', 'utf8', '-c', f"import struct,frida;from PIL import Image;assert struct.calcsize('P')=={8 if arch=='x64' else 4};print('Packaged {arch}: Python/Frida/Pillow imported')"])
run([python, '-X', 'utf8', ROOT/'codecs/test_resize.py', stage/'codecs/convert.py'])
with tempfile.TemporaryDirectory(prefix='size-', dir=ROOT/'artifacts/tests') as directory:
    fixture = Path(directory)
    for name in ['AgitCon.exe', 'AgitCon.ico', 'Microsoft.Web.WebView2.Core.dll', 'WebView2Loader.dll', 'version.json']:
        shutil.copy2(stage/name, fixture/name)
    (fixture/'codecs').mkdir()
    shutil.copy2(stage/'codecs/convert.py', fixture/'codecs/convert.py')
    (fixture/'codecs/python-path.txt').write_text(str(python), encoding='utf-8')
    try:
        run([fixture/'AgitCon.exe', '--send-size-test'])
    except subprocess.CalledProcessError:
        print((fixture/'send-size-test.txt').read_text('utf-8') if (fixture/'send-size-test.txt').exists() else 'No size-test report')
        raise
    result = (fixture/'send-size-test.txt').read_text('utf-8')
    assert result.startswith('PASS'), result
    (ROOT/f'artifacts/tests/send-size-{arch}.txt').write_text(result, encoding='utf-8')
print('PASS: packaged 100x100 preparation, animation, settings, cache and batch integration')

tools = ROOT/'autoplay/tools'
def test(name, *arguments):
    script = tools/name
    code = f"import sys,runpy;sys.path.insert(0,{str(tools)!r});sys.argv={[str(script),*arguments]!r};runpy.run_path({str(script)!r},run_name='__main__')"
    run([python, '-X', 'utf8', '-c', code])


for name in ['test-kakao-autoplay-service.py', 'test-kakao-diagnostics.py', 'test-kakao-gif-discovery.py', 'test-kakao-webp-frames.py']:
    test(name)
fixture = ROOT/'autoplay/artifacts/tests'/('autoplay-agent-load'+('-x64' if arch=='x64' else ''))
fixture.mkdir(parents=True, exist_ok=True)
import os
csc = Path(os.environ['WINDIR'])/'Microsoft.NET/Framework/v4.0.30319/csc.exe'
run([csc, '/nologo', '/target:winexe', f'/platform:{arch}', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
     f'/out:{fixture}/KakaoTalk.exe', ROOT/'autoplay/tests/Fixture.cs'])
for name in ['test-kakao-performance-native.py', 'test-kakao-resolution-native.py', 'test-kakao-refresh-native.py']:
    test(name, *(['--x64'] if arch=='x64' else []))
print(f'PASS: {arch} packaged runtime and native rendering regressions. No real KakaoTalk interaction.')
