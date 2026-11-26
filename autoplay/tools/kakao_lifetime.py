"""Read-only process identity checks; never terminate another process."""
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path


def creation_time(pid):
    api=ctypes.WinDLL('kernel32',use_last_error=True)
    api.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];api.OpenProcess.restype=wintypes.HANDLE
    api.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD];api.WaitForSingleObject.restype=wintypes.DWORD
    api.GetProcessTimes.argtypes=[wintypes.HANDLE]+[ctypes.POINTER(wintypes.FILETIME)]*4
    api.CloseHandle.argtypes=[wintypes.HANDLE]
    handle=api.OpenProcess(0x101000,False,pid)
    if not handle:return None
    try:
        if api.WaitForSingleObject(handle,0)!=258:return None
        times=[wintypes.FILETIME() for _ in range(4)]
        if not api.GetProcessTimes(handle,*[ctypes.byref(t) for t in times]):return None
        return (times[0].dwHighDateTime<<32)|times[0].dwLowDateTime
    finally:api.CloseHandle(handle)


def identity_alive(value,probe=creation_time):
    if not isinstance(value,dict):return False
    pid,start=value.get('pid'),value.get('created')
    if any(not isinstance(n,int) or isinstance(n,bool) or n<=0 for n in (pid,start)):return False
    try:return probe(pid)==start
    except (OSError,ValueError,OverflowError):return False


def owner_alive(state,probe=creation_time):
    path=Path(state)/'autoplay-owner.json'
    try:value=json.loads(path.read_text(encoding='utf-8-sig'))
    except FileNotFoundError:return True  # Legacy/explicit background launch.
    except (OSError,ValueError):return False
    return value is None or identity_alive(value,probe)


def prepare_background(state,probe=creation_time):
    if owner_alive(state,probe):return
    path=Path(state)/'autoplay-owner.json';temporary=path.with_suffix('.tmp')
    temporary.write_text('null',encoding='utf-8');os.replace(temporary,path)


def manager_alive(environment=None,probe=creation_time):
    environment=os.environ if environment is None else environment
    if 'KAKAO_AUTOPLAY_MANAGER_PID' not in environment:return True
    try:value={'pid':int(environment['KAKAO_AUTOPLAY_MANAGER_PID']),'created':int(environment['KAKAO_AUTOPLAY_MANAGER_CREATED'])}
    except (ValueError,KeyError):return False
    return identity_alive(value,probe)
