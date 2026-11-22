"""Explicit-only, hash-pinned local image-description model installer."""
import hashlib
import json
import os
from pathlib import Path
import urllib.request
from prepare import Progress

ROOT=Path(__file__).resolve().parent
SPEC='qwen3vl2b-mixed-caption-v1'
REPO='onnx-community/Qwen3-VL-2B-Instruct-ONNX'
REVISION='5dc9194ba42fa62c2f1440d98a71f7cd5280afe2'
TARGET=ROOT/'gemma2/web/models/caption'

def digest(path):
    value=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(4*1024*1024),b''):value.update(block)
    return value.hexdigest()

def install():
    manifest=json.loads((ROOT/'caption-assets.json').read_text('utf-8-sig'))
    if (manifest['spec'],manifest['repo'],manifest['revision'])!=(SPEC,REPO,REVISION):raise ValueError('Unexpected model revision')
    rows=manifest['files']
    progress=Progress([TARGET/item['path'] for item in rows],{Path(item['path']).name:item['bytes'] for item in rows})
    for item in rows:
        relative=Path(item['path'])
        if relative.drive or relative.is_absolute() or '..' in relative.parts:raise ValueError('Invalid model path')
        path=TARGET/relative
        if path.is_file() and path.stat().st_size==item['bytes'] and digest(path)==item['sha256']:
            progress.update(path,item['bytes'],size=item['bytes'],complete=True,phase='cached',force=True);continue
        path.parent.mkdir(parents=True,exist_ok=True)
        temporary=path.with_name(path.name+'.part')
        value=hashlib.sha256();total=0
        request=urllib.request.Request('https://huggingface.co/'+REPO+'/resolve/'+REVISION+'/'+item['path'],headers={'User-Agent':'AgitCon local model installer'})
        with urllib.request.urlopen(request,timeout=90) as response,temporary.open('wb') as output:
            while True:
                block=response.read(1024*1024)
                if not block:break
                total+=len(block)
                if total>item['bytes']:raise ValueError('Model size limit')
                output.write(block);value.update(block);progress.update(path,total,size=item['bytes'],downloaded=len(block))
        if total!=item['bytes'] or value.hexdigest()!=item['sha256']:raise ValueError('Model integrity mismatch')
        os.replace(temporary,path);progress.update(path,total,size=total,complete=True,force=True)
    progress.phase('verify')
    (TARGET/'ready.txt').write_text(SPEC,'utf-8');progress.phase('done')

if __name__=='__main__':install()
