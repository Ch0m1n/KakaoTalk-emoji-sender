"""Install pinned official local VLM weights and portable Vulkan runtime."""
import hashlib,json,os,zipfile
from pathlib import Path
import urllib.request
from prepare import Progress
ROOT=Path(__file__).resolve().parent
TARGET=ROOT/'caption-native'
SPEC='qwen3vl4b-q4km-vulkan-v1'
def digest(path):
    value=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(4*1024*1024),b''):value.update(block)
    return value.hexdigest()
def install():
    manifest=json.loads((ROOT/'native-caption-assets.json').read_text('utf-8-sig'))
    if manifest['spec']!=SPEC or len(manifest['files'])!=3:raise ValueError('Unexpected caption manifest')
    rows=manifest['files'];progress=Progress([TARGET/r['path'] for r in rows],{r['path']:r['bytes'] for r in rows})
    for row in rows:
        if Path(row['path']).name!=row['path']:raise ValueError('Unsafe asset path')
        path=TARGET/row['path'];path.parent.mkdir(parents=True,exist_ok=True)
        if path.is_file() and path.stat().st_size==row['bytes'] and digest(path)==row['sha256']:
            progress.update(path,row['bytes'],size=row['bytes'],complete=True,phase='cached',force=True);continue
        if not row['url'].startswith(('https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct-GGUF/resolve/1cd86afb9a95c410a6038ab3b40d8b578c892266/','https://github.com/ggml-org/llama.cpp/releases/download/b12359/')):raise ValueError('Unexpected model source')
        temporary=path.with_suffix(path.suffix+'.part');value=hashlib.sha256();count=0
        with urllib.request.urlopen(urllib.request.Request(row['url'],headers={'User-Agent':'AgitCon local model installer'}),timeout=90) as response,temporary.open('wb') as output:
            for block in iter(lambda:response.read(1024*1024),b''):
                count+=len(block)
                if count>row['bytes']:raise ValueError('Asset size limit')
                output.write(block);value.update(block);progress.update(path,count,size=row['bytes'],downloaded=len(block))
        if count!=row['bytes'] or value.hexdigest()!=row['sha256']:raise ValueError('Asset integrity mismatch')
        os.replace(temporary,path);progress.update(path,count,size=count,complete=True,force=True)
    progress.phase('verify');extracted=[]
    with zipfile.ZipFile(TARGET/'runtime.zip') as archive:
        for item in archive.infolist():
            name=Path(item.filename).name
            if item.is_dir() or not (name.endswith('.dll') or name=='llama-server.exe'):continue
            if item.file_size>256*1024*1024:raise ValueError('Runtime asset size limit')
            content=archive.read(item);sha=hashlib.sha256(content).hexdigest()
            pinned=next((r for r in manifest.get('runtime_files',[]) if r['path']==name),None)
            if manifest.get('runtime_files') and (not pinned or pinned['sha256']!=sha or pinned['bytes']!=len(content)):raise ValueError('Runtime extraction integrity mismatch')
            path=TARGET/'runtime'/name;path.parent.mkdir(parents=True,exist_ok=True);temporary=path.with_suffix(path.suffix+'.part');temporary.write_bytes(content);os.replace(temporary,path)
            extracted.append({'path':name,'bytes':len(content),'sha256':sha})
    if not any(r['path']=='llama-server.exe' for r in extracted):raise ValueError('Missing local server')
    (TARGET/'runtime-files.json').write_text(json.dumps(extracted,indent=2),'utf-8')
    (TARGET/'ready.txt').write_text(SPEC,'utf-8');progress.phase('done')
if __name__=='__main__':install()
