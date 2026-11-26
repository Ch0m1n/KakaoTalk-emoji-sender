"""Find Kakao by running process and Windows installation registration; no version/hash gate."""
import ctypes as c
from ctypes import wintypes as w
import os
from pathlib import Path
import re
import struct

KAKAO_NAMES = ('kakaotalk.exe', 'kakaotalkui.exe')


def process_path(pid):
    kernel=c.WinDLL('kernel32',use_last_error=True)
    kernel.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];kernel.OpenProcess.restype=w.HANDLE
    kernel.QueryFullProcessImageNameW.argtypes=[w.HANDLE,w.DWORD,w.LPWSTR,c.POINTER(w.DWORD)]
    kernel.CloseHandle.argtypes=[w.HANDLE]
    handle=kernel.OpenProcess(0x1000,False,pid)
    if not handle:return None
    try:
        text=c.create_unicode_buffer(32768);size=w.DWORD(len(text))
        return Path(text.value) if kernel.QueryFullProcessImageNameW(handle,0,text,c.byref(size)) else None
    finally:kernel.CloseHandle(handle)


def running_paths():
    psapi=c.WinDLL('psapi');array=(w.DWORD*4096)();needed=w.DWORD()
    psapi.EnumProcesses.argtypes=[c.POINTER(w.DWORD),w.DWORD,c.POINTER(w.DWORD)]
    if not psapi.EnumProcesses(array,c.sizeof(array),c.byref(needed)):return []
    found=[]
    for pid in array[:min(len(array),needed.value//c.sizeof(w.DWORD))]:
        path=process_path(pid)
        if path and path.name.casefold() in KAKAO_NAMES:found.append(path)
    return found


def registry_paths():
    import winreg as r
    found=[]
    def read(key,name):
        try:return r.QueryValueEx(key,name)[0]
        except OSError:return ''
    for hive in (r.HKEY_CURRENT_USER,r.HKEY_LOCAL_MACHINE):
        for view in (r.KEY_WOW64_32KEY,r.KEY_WOW64_64KEY):
            try:
                with r.OpenKey(hive,r'Software\Microsoft\Windows\CurrentVersion\App Paths\KakaoTalk.exe',0,r.KEY_READ|view) as key:found.append(read(key,''))
            except OSError:pass
            try:
                with r.OpenKey(hive,r'Software\Microsoft\Windows\CurrentVersion\Uninstall',0,r.KEY_READ|view) as parent:
                    for index in range(r.QueryInfoKey(parent)[0]):
                        try:
                            with r.OpenKey(parent,r.EnumKey(parent,index)) as key:
                                name=str(read(key,'DisplayName')).replace(' ','').casefold()
                                if 'kakaotalk' in name or '카카오톡' in name:
                                    found.extend([read(key,'InstallLocation'),read(key,'DisplayIcon')])
                        except OSError:pass
            except OSError:pass
    return found


def candidates(running=None,registered=None,environ=None):
    env=os.environ if environ is None else environ;seen=set()
    raw=list(running_paths() if running is None else running)+list(registry_paths() if registered is None else registered)
    for variable in ('ProgramFiles(x86)','ProgramFiles','LOCALAPPDATA'):
        if env.get(variable):raw.extend([Path(env[variable])/'Kakao/KakaoTalk/KakaoTalk.exe',Path(env[variable])/'KakaoTalk/KakaoTalk.exe'])
    if env.get('LOCALAPPDATA'):raw.append(Path(env['LOCALAPPDATA'])/'Programs/Kakao/KakaoTalk/bin/KakaoTalkUI.exe')
    for value in raw:
        if not value:continue
        text=os.path.expandvars(str(value)).strip();quoted=re.match(r'^"([^"]+)"(?:\s*,\s*-?\d+)?$',text)
        text=quoted.group(1) if quoted else re.sub(r',\s*-?\d+$','',text)
        path=Path(text)
        if path.suffix.casefold()!='.exe':
            qt=path/'bin/KakaoTalkUI.exe'
            path=qt if qt.is_file() else path/'KakaoTalk.exe'
        if path.name.casefold() not in KAKAO_NAMES:continue
        identity=os.path.normcase(str(path.absolute()))
        if identity not in seen:seen.add(identity);yield path


def machine(path):
    with Path(path).open('rb') as file:
        if file.read(2)!=b'MZ':raise ValueError('Not a Windows executable')
        file.seek(0x3c);offset=struct.unpack('<I',file.read(4))[0]
        if not 64<=offset<=64*1024*1024:raise ValueError('Invalid PE header')
        file.seek(offset)
        if file.read(4)!=b'PE\0\0':raise ValueError('Invalid PE signature')
        return struct.unpack('<H',file.read(2))[0]


def discover(**kwargs):
    for path in candidates(**kwargs):
        try:
            if path.is_file() and machine(path) in (0x14c,0x8664):return path
        except (OSError,ValueError,struct.error):pass
    return None


def supported_process(pid):
    path=process_path(pid)
    if path is None or path.name.casefold() not in KAKAO_NAMES:return None
    if machine(path) not in (0x14c,0x8664):raise RuntimeError('x86 또는 x64 카톡만 연결할 수 있어요.')
    return path


def x86_process(pid):
    """Legacy callers must still reject non-x86 internal layouts."""
    path=supported_process(pid)
    if path is not None and machine(path)!=0x14c:raise RuntimeError("This operation requires x86 KakaoTalk")
    return path
