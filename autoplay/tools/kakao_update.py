"""Explicit GitHub Release updates. Download first; never overwrite the running EXE."""
import hashlib
import json
import os
from pathlib import Path
import re
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

REPOSITORY='Ch0m1n/Kakao-Auto-Animated-image-play'
API='https://api.github.com/repos/'+REPOSITORY+'/releases/latest'
ASSET='KakaoAutoPlay.exe'
MAX_SIZE=200*1024*1024
ROOT=Path(__file__).resolve().parents[1]

def version(value):
    if not isinstance(value,str) or not re.fullmatch(r'v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)',value):
        raise ValueError('지원하지 않는 릴리스 버전 형식이에요.')
    return tuple(map(int,value.lstrip('v').split('.')))

def current_version():
    result=json.loads((ROOT/'version.json').read_text(encoding='utf-8-sig'))['version']
    version(result)
    return result

def allowed_url(url):
    parsed=urllib.parse.urlsplit(url)
    return parsed.scheme=='https' and parsed.port in (None,443) and not parsed.username and not parsed.password and parsed.hostname in (
        'api.github.com','github.com','release-assets.githubusercontent.com','objects.githubusercontent.com')

class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,req,fp,code,msg,headers,newurl):
        if not allowed_url(newurl):raise ValueError('허용되지 않은 업데이트 다운로드 경로예요.')
        return super().redirect_request(req,fp,code,msg,headers,newurl)

def open_url(url):
    if not allowed_url(url):raise ValueError('허용되지 않은 업데이트 주소예요.')
    request=urllib.request.Request(url,headers={'User-Agent':'KakaoAutoPlay-Updater','Accept':'application/vnd.github+json' if url==API else 'application/octet-stream'})
    return urllib.request.build_opener(SafeRedirect()).open(request,timeout=20)

def select_release(release,current):
    latest=release.get('tag_name','');version(latest)
    if release.get('draft') or release.get('prerelease'):raise ValueError('정식 릴리스만 업데이트할 수 있어요.')
    if version(latest)<=version(current):return {'available':False,'current':current,'latest':latest.lstrip('v')}
    assets=[a for a in release.get('assets',[]) if a.get('name')==ASSET and a.get('state')=='uploaded']
    if len(assets)!=1:raise ValueError('새 버전의 실행 파일이 아직 준비되지 않았어요.')
    asset=assets[0];size=asset.get('size');digest=asset.get('digest','') or ''
    url='https://github.com/'+REPOSITORY+'/releases/download/'+latest+'/'+ASSET
    if asset.get('browser_download_url')!=url:raise ValueError('릴리스 실행 파일 주소가 일치하지 않아요.')
    if type(size) is not int or not 2<=size<=MAX_SIZE or not re.fullmatch(r'sha256:[0-9a-fA-F]{64}',digest):
        raise ValueError('실행 파일의 크기 또는 SHA-256 검증 정보를 확인하지 못했어요.')
    return {'available':True,'current':current,'latest':latest.lstrip('v'),'url':url,'size':size,'sha256':digest[7:].lower()}

def check(current=None,opener=open_url):
    current=current or current_version()
    try:
        with opener(API) as response:
            data=response.read(1024*1024+1)
        if len(data)>1024*1024:raise ValueError('릴리스 응답이 너무 커요.')
        return select_release(json.loads(data),current)
    except urllib.error.HTTPError as error:
        if error.code==404:return {'available':False,'current':current,'latest':current,'notPublished':True}
        if error.code in (403,429):raise RuntimeError('GitHub 조회 한도에 도달했어요. 잠시 후 다시 확인해 주세요.') from error
        raise RuntimeError('GitHub 업데이트 확인에 실패했어요: HTTP '+str(error.code)) from error
    except (urllib.error.URLError,TimeoutError) as error:
        raise RuntimeError('GitHub에 연결하지 못했어요. 인터넷 연결을 확인해 주세요.') from error

def download(info,directory=None,opener=open_url):
    if not info.get('available'):raise ValueError('새 업데이트가 없어요.')
    version(info['latest'])
    directory=Path(directory) if directory else Path(os.environ['LOCALAPPDATA'])/'KakaoAutoPlay/updates'
    folder=directory/info['latest'];folder.mkdir(parents=True,exist_ok=True)
    target=folder/ASSET;temporary=folder/('download-'+uuid.uuid4().hex+'.part')
    try:
        size=0;digest=hashlib.sha256();started=time.monotonic();header=b''
        with opener(info['url']) as response,temporary.open('xb') as stream:
            while True:
                block=response.read(256*1024)
                if not block:break
                if not header:header=block[:2]
                size+=len(block)
                if size>info['size'] or time.monotonic()-started>300:raise ValueError('다운로드 크기 또는 시간 제한을 초과했어요.')
                digest.update(block);stream.write(block)
        if size!=info['size'] or digest.hexdigest()!=info['sha256'] or header!=b'MZ':
            raise ValueError('다운로드 검증에 실패했어요. 기존 버전은 그대로 유지해요.')
        os.replace(temporary,target)
        return target
    finally:
        temporary.unlink(missing_ok=True)

def prepare_handoff(root=ROOT,clock=time.time,pause=time.sleep):
    state=Path(root)/'artifacts/kakao-internals';path=state/'autoplay-status.json'
    try:status=json.loads(path.read_text(encoding='utf-8-sig'))
    except (OSError,ValueError):return False
    if status.get('phase')=='stopped':return False
    from kakao_lifetime import creation_time
    pid=status.get('managerPid')
    if type(pid) is not int or not creation_time(pid):return False
    (state/'autoplay-stop.signal').write_text('update',encoding='ascii')
    deadline=clock()+25
    while clock()<deadline:
        try:
            if json.loads(path.read_text(encoding='utf-8-sig')).get('phase')=='stopped':return True
        except (OSError,ValueError):pass
        if not creation_time(pid):return True
        pause(.2)
    raise RuntimeError('기존 재생 작업이 아직 종료 중이에요. 중지 후 업데이트를 다시 눌러 주세요.')

def install():
    info=check()
    if not info['available']:return info
    target=download(info)
    resume=prepare_handoff()
    return {**info,'path':str(target),'resume':resume}
