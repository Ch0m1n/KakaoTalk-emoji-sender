"""Portable entry point: bundled Python only, no shell or package installer."""
import argparse
import ctypes
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import winreg
import re
sys.path.insert(0, str(Path(__file__).resolve().parent))
from kakao_diagnostics import event, export as export_diagnostics
from kakao_lifetime import owner_alive, prepare_background
from kakao_install import discover

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / 'artifacts/kakao-internals'
PYTHON = ROOT.parent.parent / 'runtime/python/pythonw.exe'
SERVICE = ROOT / 'tools/kakao-autoplay-service.py'
RUN_KEY = r'Software\Microsoft\Windows\CurrentVersion\Run'
RUN_NAME = 'KakaoGifAutoPlay'


def stop_previous_bundles(root=None, clock=time.time, pause=time.sleep):
    root = Path(root or ROOT).resolve()
    if root.parent.name != 'bundles' or root.parent.parent.name != 'KakaoAutoPlay' or not re.fullmatch('[0-9a-f]{16}',root.name):
        return []
    previous = []
    for candidate in root.parent.iterdir():
        if candidate == root or not candidate.is_dir() or not re.fullmatch('[0-9a-f]{16}',candidate.name):
            continue
        if candidate.resolve().parent != root.parent:
            continue
        state = candidate/'artifacts/kakao-internals'
        if not state.resolve().is_relative_to(candidate.resolve()):
            continue
        if not (candidate/'tools/kakao-autoplay-service.py').is_file() or not (candidate/'runtime/pythonw.exe').is_file():
            continue
        try:
            status = json.loads((state/'autoplay-status.json').read_text(encoding='utf-8-sig'))
            if status.get('phase') == 'stopped' or not -5 <= clock()-float(status['updated']) <= 15:
                continue
            if not isinstance(status.get('managerPid'),int) or status['managerPid'] <= 0:
                continue
            (state/'autoplay-stop.signal').write_text('stop',encoding='ascii')
            previous.append(state)
        except (OSError,ValueError,KeyError,TypeError):
            continue
    for _ in range(80):
        waiting = []
        for state in previous:
            try:
                if json.loads((state/'autoplay-status.json').read_text(encoding='utf-8-sig')).get('phase') == 'stopped':
                    continue
            except (OSError,ValueError):
                pass
            waiting.append(state)
        if not waiting:
            return [state.parent.parent for state in previous]
        pause(.1)
    raise RuntimeError('이전 버전 도우미가 종료 처리 중이에요. 기존 도우미에서 중지를 누른 뒤 다시 시작해주세요.')


def startup_command():
    exe=os.environ.get('KAKAO_AUTOPLAY_EXE')
    if exe:
        path=Path(exe)
        if not path.is_absolute() or path.suffix.lower()!='.exe' or not path.is_file():
            raise ValueError('자동 시작 실행 파일 경로를 확인하지 못했어요.')
        return subprocess.list2cmdline([str(path),'--tray','--resume'])
    return subprocess.list2cmdline([str(PYTHON),str(Path(__file__).resolve()),'--background'])


def known_startup(command):
    # Recognize exact argument shapes, never execute the saved command.
    tokens=re.findall(r'"[^"\r\n]*"|[^\s"]+',command)
    if ' '.join(tokens)!=command.strip():return False
    args=[value.strip('"') for value in tokens]
    if len(args)==3 and args[1:]==['--tray','--resume']:
        path=Path(args[0])
        if not path.is_file() or not path.name.lower().startswith('kakaoautoplay') or path.suffix.lower()!='.exe':return False
        # Our launcher embeds a named payload; unrelated executables are not adopted.
        if path.stat().st_size>200*1024*1024:return False
        with path.open('rb') as stream:
            if stream.read(2)!=b'MZ':return False
            tail=b''
            while True:
                block=stream.read(1024*1024)
                if not block:return False
                data=tail+block
                if b'payload.zip' in data:return True
                tail=data[-16:]
    if len(args) not in (2,3) or (len(args)==3 and args[2]!='--background'):return False
    if Path(args[0]).name.lower() not in ('python.exe','pythonw.exe'):return False
    script=Path(args[1])
    if script.name not in ('kakao-portable-launcher.py','kakao-autoplay-service.py') or not script.is_file():return False
    source=script.read_text(encoding='utf-8-sig')
    return 'kakao-internals' in source and ('KakaoGifAutoPlay' in source or 'kakao-bitmap-bridge.js' in source)


def migrate_existing_startup(previous=None):
    known={subprocess.list2cmdline([str(old/'runtime/pythonw.exe'),str(old/'tools/kakao-portable-launcher.py'),'--background']) for old in (previous or [])}
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY,0,winreg.KEY_QUERY_VALUE|winreg.KEY_SET_VALUE) as key:
            current=winreg.QueryValueEx(key,RUN_NAME)[0]
            command=startup_command()
            if current==command:return False
            if current not in known and not known_startup(current):
                raise RuntimeError('자동 시작 항목을 확인하지 못해 유지했어요. 등록 명령: '+current)
            STATE.mkdir(parents=True,exist_ok=True)
            (STATE/'startup-migration.json').write_text(json.dumps({'previous':current,'current':command},ensure_ascii=False),encoding='utf-8')
            winreg.SetValueEx(key,RUN_NAME,0,winreg.REG_SZ,command)
            return True
    except FileNotFoundError:
        return False


def stop_migrated_service():
    try:command=json.loads((STATE/'startup-migration.json').read_text(encoding='utf-8'))['previous']
    except (FileNotFoundError,ValueError,KeyError):return
    if not known_startup(command):return
    args=[value.strip('"') for value in re.findall(r'"[^"\r\n]*"|[^\s"]+',command)]
    if len(args)<2 or Path(args[1]).suffix!='.py':return
    old=Path(args[1]).resolve().parents[1]
    if old==ROOT.resolve():return
    from kakao_update import prepare_handoff
    prepare_handoff(old)


def check():
    sys.path.insert(0, str(ROOT.parent / 'deps'))
    sys.path.insert(0, str(ROOT.parent / 'deps'))
    import frida
    from PIL import Image
    for format in ('GIF', 'WEBP'):
        buffer = io.BytesIO()
        Image.new('RGB', (8,8), 'red').save(buffer, format=format, save_all=True,
            append_images=[Image.new('RGB', (8,8), 'blue')], duration=100, loop=0)
        buffer.seek(0)
        with Image.open(buffer) as animation:
            if animation.n_frames != 2:
                raise RuntimeError(format + ' 애니메이션 디코더를 불러오지 못했어요.')
            animation.seek(1)
            animation.load()
    detected=discover()
    return {'python':sys.version.split()[0], 'frida':frida.__version__, 'pillow':Image.__version__, 'supported':True,'kakaoDetected':detected is not None}


def startup(enable):
    if os.environ.get('AGITCON_INTEGRATED') == '1':
        return  # The parent app owns login startup; never touch legacy registrations.
    command = startup_command()
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
        try:
            current = winreg.QueryValueEx(key, RUN_NAME)[0]
        except FileNotFoundError:
            current = None
        if current and current != command and not known_startup(current):
            raise RuntimeError('알 수 없는 자동 시작 등록을 유지했어요. 등록 명령: '+current)
        if enable:
            winreg.SetValueEx(key, RUN_NAME, 0, winreg.REG_SZ, command)
        elif current:
            winreg.DeleteValue(key, RUN_NAME)


def run(action, output=None, background=False):
    STATE.mkdir(parents=True, exist_ok=True)
    if action in ('update-check','update-install'):
        import kakao_update
        return kakao_update.check() if action=='update-check' else kakao_update.install()
    if action == 'startup-migrate':
        changed=migrate_existing_startup()
        return '기존 자동 시작 설정을 새 버전으로 전환했어요.' if changed else 'X 버튼 동작은 옵션에서 바꿀 수 있어요. 완전 종료는 트레이 메뉴를 이용해 주세요.'
    if action == 'update-adopt':
        previous=Path(output or '').resolve()
        base=Path(os.environ['LOCALAPPDATA'])/'KakaoAutoPlay/bundles'
        if previous.parent!=base.resolve() or not re.fullmatch('[0-9a-f]{16}',previous.name):
            raise ValueError('이전 버전 경로가 올바르지 않아요.')
        migrate_existing_startup([previous])
        return '업데이트 설정을 이어받았어요.'
    if action == 'diagnostics':
        return '진단 ZIP을 저장했어요.\n' + export_diagnostics(output)
    if action == 'stop':
        (STATE/'autoplay-stop.signal').write_text('stop', encoding='ascii')
        startup(False)
        return '중지를 요청하고 로그인 자동 시작을 해제했어요.\n정리에는 잠시 시간이 걸릴 수 있어요.'
    result = check()
    if action == 'check':
        return result
    if background:prepare_background(STATE)
    elif not owner_alive(STATE):return '도우미 창이 종료되어 재생을 시작하지 않았어요.'
    if os.environ.get('AGITCON_INTEGRATED') != '1':
        migrate_existing_startup(stop_previous_bundles())
        stop_migrated_service()
    if action == 'startup':
        startup(True)
    # Wait for any previous stop request to finish before removing its signal.
    signal = STATE/'autoplay-stop.signal'
    status_file = STATE/'autoplay-status.json'
    if signal.exists() and status_file.exists():
        for _ in range(100):
            try:
                status = json.loads(status_file.read_text(encoding='utf-8'))
                if status['phase'] == 'stopped' or time.time()-status['updated'] > 8:
                    break
            except (ValueError, OSError, KeyError):
                pass
            time.sleep(.2)
        else:
            raise RuntimeError('이전 실행이 정리 중이에요. 잠시 후 다시 시작해 주세요.')
    signal.unlink(missing_ok=True)
    if not owner_alive(STATE):return '도우미 창이 종료되어 재생을 시작하지 않았어요.'
    with (STATE/'portable-launch.log').open('w', encoding='utf-8') as log:
        subprocess.Popen([str(PYTHON), str(SERVICE)], cwd=ROOT, stdout=log, stderr=log,
                         creationflags=subprocess.CREATE_NO_WINDOW)
    for _ in range(75):
        if not owner_alive(STATE):return '도우미 창이 종료되어 재생을 정리하고 있어요.'
        try:
            status = json.loads(status_file.read_text(encoding='utf-8'))
            if time.time()-status['updated'] < 5 and status['phase'] not in ('stopped','stopping'):
                return '자동재생 도우미가 실행 중이에요.\n카톡에서 GIF가 있는 채팅방을 열어 주세요.' + ('\n로그인 자동 시작도 등록했어요.' if action == 'startup' else '')
        except (ValueError, OSError, KeyError):
            pass
        time.sleep(.2)
    raise RuntimeError('실행 상태를 확인하지 못했어요.\nartifacts/kakao-internals 폴더의 로그를 확인해 주세요.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('action', nargs='?', choices=['start','stop','startup','check','diagnostics','update-check','update-install','update-adopt','startup-migrate'], default='start')
    parser.add_argument('--background', action='store_true')
    parser.add_argument('--json', action='store_true')
    parser.add_argument('--output')
    args = parser.parse_args()
    try:
        event('action_started', action=args.action, python=sys.version.split()[0])
        message = run(args.action, args.output, args.background)
        event('action_completed', action=args.action)
        if args.json or args.action == 'check':
            print(json.dumps({'ok':True,'message':message},ensure_ascii=False))
        elif message and not args.background:
            ctypes.windll.user32.MessageBoxW(None, message, 'Kakao AutoPlay', 0x40)
    except Exception as error:
        event('action_failed', action=args.action, error=str(error), errorType=type(error).__name__)
        STATE.mkdir(parents=True, exist_ok=True)
        (STATE/'portable-error.txt').write_text(str(error), encoding='utf-8')
        if args.json:
            print(json.dumps({'ok':False,'message':str(error)},ensure_ascii=False))
        elif not args.background and args.action != 'check':
            ctypes.windll.user32.MessageBoxW(None, str(error), 'Kakao AutoPlay', 0x10)
        else:
            if sys.stderr:
                print(str(error), file=sys.stderr)
        sys.exit(1)
