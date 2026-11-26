"""Build a clean Windows x64 portable bundle from the verified local runtime."""
from pathlib import Path
import shutil
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT/'artifacts/KakaoAutoPlay-portable'
RUNTIME = Path(sys.executable).parent
DEST.mkdir(parents=True, exist_ok=True)
for relative in ['tools', 'runtime', 'artifacts/kakao-internals/deps', 'dist/codecs/deps']:
    (DEST/relative).mkdir(parents=True, exist_ok=True)
shutil.copy2(ROOT/'version.json', DEST/'version.json')
for name in ['kakao-portable-launcher.py','kakao_update.py','kakao_diagnostics.py','kakao-autoplay-service.py','trace-kakao-player.py',
             'kakao-bitmap-bridge.js','kakao-auto-bridge.js','kakao-original-download.js','kakao-photo-hover.js','kakao-compact-media.js',
             'kakao_qt_worker.py','kakao-qt-live.js','kakao-qt-photo.qml',
             'kakao-gif-discovery.py','kakao-webp-frames.py','kakao_download_startup.py','kakao_lifetime.py','kakao_install.py','KAKAO-GIF-EXPERIMENT.md']:
    shutil.copy2(ROOT/'tools'/name, DEST/'tools'/name)
for relative in ['artifacts/kakao-internals/deps/frida','artifacts/kakao-internals/deps/frida-17.27.4.dist-info',
                 'dist/codecs/deps/PIL','dist/codecs/deps/pillow-13.0.0.dist-info']:
    shutil.copytree(ROOT/relative, DEST/relative, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns('__pycache__','*.pyc'))
for name in ['python.exe','pythonw.exe','python3.dll','python311.dll','vcruntime140.dll','vcruntime140_1.dll','LICENSE.txt']:
    shutil.copy2(RUNTIME/name, DEST/'runtime'/name)
shutil.copytree(RUNTIME/'Lib', DEST/'runtime/Lib', dirs_exist_ok=True,
    ignore=shutil.ignore_patterns('site-packages','__pycache__','*.pyc','test','tests','idlelib','tkinter','ensurepip'))
shutil.copytree(RUNTIME/'DLLs', DEST/'runtime/DLLs', dirs_exist_ok=True,
    ignore=shutil.ignore_patterns('*_test*','tcl*','tk*','_tkinter*'))
(DEST/'runtime/python311._pth').write_text('Lib\nDLLs\n.\n', encoding='ascii')
for label, action in [('시작','start'),('중지','stop'),('로그인시_자동시작_등록','startup')]:
    (DEST/(label+'.cmd')).write_bytes(('@echo off\r\nstart "" "%~dp0runtime\\pythonw.exe" "%~dp0tools\\kakao-portable-launcher.py" '+action+'\r\n').encode('ascii'))
(DEST/'사용방법.txt').write_text('''Kakao AutoPlay 개인 테스트용 / Windows x64

Python, PowerShell 추가 설치가 필요하지 않습니다.
1. ZIP 전체를 쓰기 가능한 폴더에 압축 해제하세요. ZIP 안에서 바로 실행하지 마세요.
2. 시작.cmd를 더블클릭하고 카톡에서 GIF가 있는 채팅방을 여세요.
3. 종료하려면 중지.cmd를 더블클릭하세요. 로그인 자동 시작도 함께 해제합니다.
4. 원할 때만 로그인시_자동시작_등록.cmd를 실행하세요.
   폴더 이동 전에는 중지하고, 이동 후 다시 등록하세요.

32/64비트 카톡의 실행 경로를 자동 탐지해요. Qt 64비트(KakaoTalkUI.exe)는 개별·묶음 사진 원본 자동 다운로드와 반복재생을 지원해요. 기존 64비트(KakaoTalk.exe)는 저장된 원본 재생만 시험 지원해요. 버전 번호·파일 해시로 실행을 막지 않아요.
실행 중인 카톡 → Windows 설치 등록 정보 → 기본 폴더 순서로 찾아요.
내부 원본 다운로드 코드는 구조가 일치할 때만 연결해요. 다른 구조에서는 저장된 원본 재생만 시도해요.
이전 자동재생 도우미가 있다면 먼저 중지하세요. 서로 다른 폴더의 도우미를 동시에 켜지 마세요.
이 패키지는 설치된 카톡 파일을 바꾸지 않고 실행 중인 프로세스에 연결합니다.
다운로드 원본과 준비 프레임은 이 폴더 아래 artifacts에 저장됩니다.
아직 디스크 캐시 자동 정리는 없으므로 폴더 용량에 유의하세요.
노트북의 실제 실행은 별도 검증이 필요합니다. 상세 제한은 tools/KAKAO-GIF-EXPERIMENT.md에 있습니다.
오류: artifacts/kakao-internals/portable-error.txt 및 autoplay-worker.stderr.log
Python 라이선스: runtime/LICENSE.txt, Frida/Pillow 라이선스: 각 dist-info 폴더.
''', encoding='utf-8-sig')
print(DEST)
