"""User-session supervisor for the version-locked Kakao GIF bridge (no UI input)."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid
sys.path.insert(0, str(Path(__file__).resolve().parent))
from kakao_diagnostics import event, worker_failure
from kakao_lifetime import owner_alive, creation_time
from kakao_install import machine, KAKAO_NAMES

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / 'artifacts/kakao-internals'



def atomic_json(path, value):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    os.replace(temporary, path)


class Windows:
    def __init__(self):
        self.connection_error=None
        self.u = c.WinDLL('user32', use_last_error=True)
        self.k = c.WinDLL('kernel32', use_last_error=True)
        self.callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
        self.u.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
        self.u.GetWindowThreadProcessId.restype = w.DWORD
        self.u.GetClassNameW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
        self.u.EnumWindows.argtypes = [self.callback, w.LPARAM]
        self.u.EnumChildWindows.argtypes = [w.HWND, self.callback, w.LPARAM]
        self.k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
        self.k.OpenProcess.restype = w.HANDLE
        self.k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
        self.k.GetProcessTimes.argtypes = [w.HANDLE] + [c.POINTER(w.FILETIME)] * 4
        self.k.CloseHandle.argtypes = [w.HANDLE]
        self.k.CreateMutexW.argtypes = [c.c_void_p, w.BOOL, w.LPCWSTR]
        self.k.CreateMutexW.restype = w.HANDLE

    def mutex(self):
        name = 'Local\\KakaoGifAutoPlay.UserSession'
        handle = self.k.CreateMutexW(None, False, name)
        if not handle:
            raise c.WinError(c.get_last_error())
        if c.get_last_error() == 183:
            self.k.CloseHandle(handle)
            return None
        return handle

    def classname(self, hwnd):
        value = c.create_unicode_buffer(256)
        self.u.GetClassNameW(hwnd, value, len(value))
        return value.value

    def process(self, pid):
        handle = self.k.OpenProcess(0x1000, False, pid)
        if not handle:
            return None
        try:
            path = c.create_unicode_buffer(32768)
            size = w.DWORD(len(path))
            if not self.k.QueryFullProcessImageNameW(handle, 0, path, c.byref(size)):
                return None
            if Path(path.value).name.casefold() not in KAKAO_NAMES:
                return None
            if machine(Path(path.value)) not in (0x14c,0x8664):
                self.connection_error='x86 또는 x64 카톡만 연결할 수 있어요.'
                return None
            times = [w.FILETIME() for _ in range(4)]
            if not self.k.GetProcessTimes(handle, *[c.byref(t) for t in times]):
                return None
            return (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
        finally:
            self.k.CloseHandle(handle)

    def inventory(self):
        self.connection_error=None
        groups, alive, processes = {}, set(), {}

        def visit(hwnd, _):
            cls=self.classname(hwnd)
            qt=cls.startswith('Qt') and 'QWindow' in cls
            if cls != 'EVA_Window_Dblclk' and not qt:
                return True
            pid = w.DWORD()
            thread = self.u.GetWindowThreadProcessId(hwnd, c.byref(pid))
            if pid.value not in processes:
                processes[pid.value] = self.process(pid.value)
            created = processes[pid.value]
            if created is None:
                return True
            alive.add((pid.value, created))
            rich = []

            def child(handle, _):
                if self.classname(handle).upper() == 'RICHEDIT50W':
                    rich.append(True)
                return True

            self.u.EnumChildWindows(hwnd, self.callback(child), 0)
            if rich or qt:
                groups.setdefault((pid.value, created, thread), []).append(int(hwnd))
            return True

        self.u.EnumWindows(self.callback(visit), 0)
        return groups, alive


class Supervisor:
    """One worker at a time; handle changes update it without duplicate hooks."""
    def __init__(self, spawn, control, clock=time.monotonic):
        self.spawn, self.control, self.clock = spawn, control, clock
        self.worker = None
        self.key = None
        self.retry_at = 0
        self.generation = 0
        self.phase = 'waiting'
        self.stopping = False
        self.last_exit = None

    def tick(self, groups, alive, stop=False):
        self.stopping |= stop
        if self.worker and self.worker.poll() is not None:
            self.last_exit = self.worker.poll()
            self.worker.close()
            self.worker = None
            self.retry_at = self.clock() + 3
        if self.worker:
            if self.stopping or self.key[:2] not in alive:
                self.worker.request_stop()
                self.phase = 'stopping' if self.stopping else 'reconnecting'
            else:
                windows = groups.get(self.key, [])
                self.control(self.key, windows)
                self.phase = 'running' if windows else 'waiting-for-chat'
            return
        if self.stopping:
            self.phase = 'stopped'
        elif groups and self.clock() >= self.retry_at:
            self.key = sorted(groups)[0]
            self.control(self.key, groups[self.key])
            self.worker = self.spawn(self.key, groups[self.key][0])
            self.generation += 1
            self.phase = 'starting'
        else:
            self.phase = 'waiting'


class Worker:
    def __init__(self, key, hwnd):
        self.signal = STATE / ('bridge-stop-' + uuid.uuid4().hex + '.signal')
        self.out = (STATE / 'autoplay-worker.stdout.log').open('w', encoding='utf-8')
        self.err = (STATE / 'autoplay-worker.stderr.log').open('w', encoding='utf-8')
        environment=dict(os.environ)
        environment['KAKAO_AUTOPLAY_MANAGER_PID']=str(os.getpid())
        environment['KAKAO_AUTOPLAY_MANAGER_CREATED']=str(creation_time(os.getpid()))
        self.process = subprocess.Popen([
            sys.executable, str(ROOT/'tools/trace-kakao-player.py'), '--pid', str(key[0]),
            '--seconds', '0', '--bridge', '--auto-gifs', '--download-originals',
            '--chat-window', str(hwnd), '--control-file', str(STATE/'autoplay-control.json'),
            '--stop-file', str(self.signal)], cwd=ROOT, stdout=self.out, stderr=self.err,
            creationflags=subprocess.CREATE_NO_WINDOW,env=environment)

    def poll(self):
        return self.process.poll()

    def request_stop(self):
        self.signal.write_text('stop', encoding='ascii')

    def close(self):
        self.out.close()
        self.err.close()
        if self.process.poll():
            try:
                report = json.loads((STATE/'bitmap-bridge.json').read_text(encoding='utf-8-sig'))
            except (OSError, ValueError):
                report = {}
            try:
                stderr = (STATE/'autoplay-worker.stderr.log').read_text(encoding='utf-8', errors='replace')[-65536:]
            except OSError:
                stderr = ''
            summary = worker_failure(report, stderr)
            summary['exitCode'] = self.process.poll()
            atomic_json(STATE/'last-worker-failure.json', summary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inventory', action='store_true')
    args = parser.parse_args()
    windows = Windows()
    if args.inventory:
        groups, _ = windows.inventory()
        print(json.dumps([{'pid':k[0], 'thread':k[2], 'windows':v} for k,v in groups.items()]))
        return
    STATE.mkdir(exist_ok=True)
    mutex = windows.mutex()
    if mutex is None:
        return
    stop_file = STATE/'autoplay-stop.signal'
    control_state = None

    def control(key, handles):
        nonlocal control_state
        value = {'pid':key[0], 'windows':sorted(handles)}
        if value != control_state:
            atomic_json(STATE/'autoplay-control.json', value)
            control_state = value

    supervisor = Supervisor(Worker, control)
    version_stamp = None
    supported = False
    previous_phase = None
    previous_error = None
    try:
        while True:
            try:
                supported = True
                groups, alive = windows.inventory()
                if not owner_alive(STATE):
                    stop_file.write_text('owner-exited',encoding='ascii')
                supervisor.tick(groups if supported else {}, alive, stop_file.exists() or not supported)
                transition = (supervisor.phase, supervisor.generation, supported)
                if transition != previous_phase:
                    event('service_state', phase=supervisor.phase, generation=supervisor.generation, supported=supported)
                    previous_phase = transition
                atomic_json(STATE/'autoplay-status.json', {
                    'managerPid':os.getpid(), 'workerPid':supervisor.worker.process.pid if supervisor.worker else None,
                    'phase':supervisor.phase, 'generation':supervisor.generation,
                    'connectionError':windows.connection_error, 'supportedVersion':supported, 'windows':control_state['windows'] if control_state else [],
                    'updated':time.time(), 'unlimited':True, 'lastWorkerExit':supervisor.last_exit})
                if supervisor.phase == 'stopped':
                    break
            except (OSError, ValueError) as error:
                if str(error) != previous_error:
                    event('service_error', error=str(error), errorType=type(error).__name__)
                    previous_error = str(error)
                atomic_json(STATE/'autoplay-error.json', {'error':str(error), 'updated':time.time()})
            time.sleep(2)
    finally:
        if supervisor.worker:
            supervisor.worker.request_stop()
            while supervisor.worker.poll() is None:
                time.sleep(.5)
            supervisor.worker.close()
        windows.k.CloseHandle(mutex)


if __name__ == '__main__':
    main()
