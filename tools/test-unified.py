"""Exercise packaged Python and owned native fixtures, never the KakaoTalk process."""
import argparse
import json
import subprocess
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
