"""Local animated GIF/WebP -> cached animated WebP. Never accesses clipboard or network."""
import hashlib,json,os,sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent.parent/'codecs'/'deps'))
from PIL import Image
SPEC='animated-webp-lossless-v3'
def prepare(source,folder):
 source,folder=Path(source),Path(folder)
 if source.stat().st_size>30*1024*1024:raise ValueError('Animation exceeds 30 MB')
 original=source.read_bytes();digest=hashlib.sha256(original).hexdigest();folder.mkdir(parents=True,exist_ok=True)
 target=folder/(digest+'.webp');receipt=target.with_suffix('.json')
 try:
  meta=json.loads(receipt.read_text('utf-8'))
  if meta['spec']==SPEC and meta['source']==digest and hashlib.sha256(target.read_bytes()).hexdigest()==meta['sha256']:return meta
 except (OSError,ValueError,KeyError,TypeError):pass
 frames=[];durations=[]
 with Image.open(source) as image:
  if image.format not in ('GIF','WEBP'):raise ValueError('Expected GIF or WebP')
  kind=image.format;count=getattr(image,'n_frames',1)
  if count<2 or count>900 or image.width>2048 or image.height>2048 or image.width*image.height*count>40_000_000:raise ValueError('Unsupported animation dimensions/frame count')
  loop=image.info.get('loop')
  # GIF extension counts repetitions after the first play; WebP counts total plays.
  output_loop=(1 if loop is None else 0 if loop==0 else loop+1) if kind=='GIF' else (loop if loop is not None else 1)
  if output_loop>65535:raise ValueError('Loop count not representable in WebP')
  for i in range(count):
   image.seek(i);image.load();duration=int(image.info.get('duration',100))
   if duration<0:raise ValueError('Negative frame duration')
   durations.append(duration);frame=image.convert('RGBA').copy();frame.info.clear();frames.append(frame)
 if sum(durations)>30000:raise ValueError('Animation exceeds 30 seconds')
 fd,name=tempfile.mkstemp(suffix='.webp',dir=folder);os.close(fd)
 try:
  if kind=='WEBP':Path(name).write_bytes(original)
  else:frames[0].save(name,format='WEBP',save_all=True,append_images=frames[1:],duration=durations,loop=output_loop,lossless=True,quality=100,method=4,exact=True,kmin=1,kmax=1,background=(0,0,0,0))
  with Image.open(name) as result:
   if result.format!='WEBP' or result.n_frames<2:raise ValueError('Output lost animation')
   total=0
   for i in range(result.n_frames):result.seek(i);result.load();total+=int(result.info.get('duration',0))
   if total!=sum(durations) or result.info.get('loop')!=output_loop:raise ValueError('Animation timing/loop mismatch')
   out_frames=result.n_frames
  raw=Path(name).read_bytes()
  if len(raw)>30*1024*1024:raise ValueError('Output exceeds 30 MB')
  meta={'spec':SPEC,'source':digest,'sha256':hashlib.sha256(raw).hexdigest(),'length':len(raw),'frames':out_frames,'duration':sum(durations),'loop':output_loop,'path':str(target.resolve())}
  os.replace(name,target);temp=receipt.with_name(receipt.name+'.'+os.urandom(6).hex()+'.tmp')
  try:temp.write_text(json.dumps(meta),'utf-8');os.replace(temp,receipt)
  finally:
   if temp.exists():temp.unlink()
  return meta
 finally:
  if Path(name).exists():Path(name).unlink()
if __name__=='__main__':
 try:print(json.dumps(prepare(sys.argv[1],sys.argv[2])),flush=True)
 except Exception as error:print(json.dumps({'error':str(error)}),flush=True);sys.exit(1)
