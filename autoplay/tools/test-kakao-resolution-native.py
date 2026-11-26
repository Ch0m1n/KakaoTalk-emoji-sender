"""Exercise shipped Frida/GDI code only in our own hidden fixture, never Kakao."""
import json, subprocess, sys, time
from pathlib import Path
ARCH='x64' if '--x64' in sys.argv else 'x86'
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'artifacts/kakao-internals/deps'))
sys.path.insert(0,str(ROOT/'dist/codecs/deps'))
import frida
from PIL import Image, ImageDraw
work=ROOT/'artifacts/tests/resolution-playback';work.mkdir(parents=True,exist_ok=True)
identity=work/'fixture.json';identity.unlink(missing_ok=True)
process=subprocess.Popen([str(ROOT/('artifacts/tests/autoplay-agent-load'+('-x64' if ARCH=='x64' else '')+'/KakaoTalk.exe')),str(identity)],creationflags=0x08000000)
session=None
try:
    # A fresh hosted Windows runner may take longer than five seconds to start
    # the .NET fixture. Still fail on process exit or an explicit deadline.
    deadline=time.monotonic()+45
    while not identity.exists():
        if process.poll() is not None:
            raise RuntimeError(f'Native {ARCH} fixture exited before readiness: {process.returncode}')
        if time.monotonic() >= deadline:
            raise TimeoutError(f'Native {ARCH} fixture did not publish readiness within 45 seconds')
        time.sleep(.05)
    info=json.loads(identity.read_text());assert info['pid']==process.pid and process.poll() is None
    session=frida.attach(process.pid)
    base=(ROOT/'tools/kakao-bitmap-bridge.js').read_text(encoding='utf-8-sig').split('const hwnd=')[0]
    code='const BRIDGE_CONFIG='+json.dumps({'hwnd':info['hwnd'],'managed':True})+';\n'+base+(ROOT/'tools/kakao-auto-bridge.js').read_text(encoding='utf-8-sig')
    code+='''
rpc.exports.probefile=function(path){return open(path,image=>{
 const a=Memory.alloc(Process.pointerSize),b=Memory.alloc(Process.pointerSize);getWidth(image,a);const w=a.readU32();getHeight(image,b);const h=b.readU32();
 if(makeBitmap(image,a,0xffffffff)!==0)throw Error('fixture bitmap');const bitmap=a.readPointer();
 try{return match(bitmap,w,h);}finally{deleteObject(bitmap);}
});};
rpc.exports.frames=function(id){
 const s=sources.get(id);if(!s||!s.frames)throw Error('Decoded frames missing: '+JSON.stringify(s&&s.report));
 for(const f of s.frames){const b=Memory.alloc(BITMAP_SIZE);if(getObject(f.bitmap,BITMAP_SIZE,b)!==BITMAP_SIZE||b.add(4).readS32()!==s.w)throw Error('Invalid native frame');}
 return {ends:s.frames.map(f=>f.end),duration:s.duration,arch:Process.arch};
};
rpc.exports.benchmark=function(path){return open(path,image=>{
 const out=Memory.alloc(Process.pointerSize);getWidth(image,out);const w=out.readU32();getHeight(image,out);const h=out.readU32();
 if(makeBitmap(image,out,0xffffffff)!==0)throw Error('fixture bitmap');const bitmap=out.readPointer();
 try{
  clearMatches();match(bitmap,w,h);
  const hits=stats.performance.bitmapCacheHits,started=Date.now();
  for(let i=0;i<100;i++)if(!match(bitmap,w,h))throw Error('benchmark match');
  const cachedMs=Date.now()-started,cacheHits=stats.performance.bitmapCacheHits-hits;
  if(cacheHits!==100)throw Error('DIB cache not exercised');
  const view=bitmapView(bitmap,w,h),last=view.bits.add(view.size-1),byte=last.readU8();last.writeU8(byte^1);
  match(bitmap,w,h);if(stats.performance.bitmapCacheHits!==hits+100)throw Error('Last-byte mutation missed');last.writeU8(byte);
  const originalView=bitmapView;let normalizedMs;
  try{
    bitmapView=()=>null;clearMatches();match(bitmap,w,h);
    const started=Date.now();
    for(let i=0;i<100;i++)if(!match(bitmap,w,h))throw Error('normalized benchmark match');
    normalizedMs=Date.now()-started;
  }finally{bitmapView=originalView;}
  return {iterations:100,cachedMs,normalizedMs,cacheHits};
 }finally{deleteObject(bitmap);}
});};
'''
    script=session.create_script(code,runtime='v8');errors=[];script.on('message',lambda message,data:errors.append(message) if message['type']=='error' else None);script.load();api=script.exports_sync
    results=[]
    for number in range(2):
        frames=[]
        for frame in range(2):
            im=Image.new('RGB',(200,200),'white');d=ImageDraw.Draw(im)
            d.rectangle((8,15,85,135),fill='red' if number==0 else 'green');d.ellipse((75,65,190,190),fill='blue' if number==0 else 'orange')
            d.line((0,180-frame*15,180,0),fill='black',width=7);frames.append(im)
        gif=work/f'source-{number}.gif';frames[0].save(gif,save_all=True,append_images=frames[1:],duration=[70,130],loop=0)
        assert api.registerfile(str(gif),f'fixture-{number}')['status']=='registered'
        api.activate(f'fixture-{number}')
        decoded=api.frames(f'fixture-{number}')
        assert decoded['ends']==[70,200] and decoded['duration']==200,decoded
        with Image.open(gif) as original: first=original.convert('RGB')
        for size in [100,150,200,250,300,350,400,600,800]:
            for method in [Image.Resampling.NEAREST,Image.Resampling.BILINEAR,Image.Resampling.BICUBIC]:
                png=work/f'preview-{number}-{size}-{method}.png';first.resize((size,size),method).save(png)
                matched=api.probefile(str(png));found=matched['id'] if matched else None;results.append({'source':number,'size':size,'filter':int(method),'match':found})
                if found!=f'fixture-{number}':print('MISS',results[-1],api.snapshot()['comparisons'][-1:],flush=True)
    unrelated=work/'unrelated.png';Image.new('RGB',(300,300),'purple').save(unrelated);assert api.probefile(str(unrelated)) is None
    # Transparent, portrait sources are also displayed flattened onto different
    # backgrounds by GDI. Reference variants cover both white and black.
    frames=[]
    for offset in [0,12]:
        im=Image.new('RGBA',(140,200),(0,0,0,0));d=ImageDraw.Draw(im)
        d.rectangle((15,20+offset,65,120),fill=(255,30,20,255));d.ellipse((60,85,125,180),fill=(20,50,255,255));frames.append(im)
    transparent=work/'transparent.gif';frames[0].save(transparent,save_all=True,append_images=frames[1:],duration=100,loop=0,disposal=2)
    assert api.registerfile(str(transparent),'transparent')['status']=='registered'
    with Image.open(transparent) as original:first=original.convert('RGBA')
    for scale in [.75,1,1.5,2,3,4]:
        for background in ['white','black']:
            im=Image.new('RGBA',first.size,background);im.alpha_composite(first)
            png=work/f'transparent-{scale}-{background}.png';im.convert('RGB').resize((round(140*scale),round(200*scale)),Image.Resampling.BICUBIC).save(png)
            matched=api.probefile(str(png));found=matched['id'] if matched else None;assert found=='transparent',(scale,background,found,api.snapshot()['comparisons'][-1:])
    # Exercise the shipped animated WebP -> PNG -> native bitmap pipeline too.
    import hashlib, importlib.util
    spec=importlib.util.spec_from_file_location('webp_frames',ROOT/'tools/kakao-webp-frames.py')
    webp_module=importlib.util.module_from_spec(spec);spec.loader.exec_module(webp_module)
    webp=work/'source.webp';frames[0].save(webp,save_all=True,append_images=frames[1:],duration=[80,120],loop=0,lossless=True)
    key=hashlib.sha256(webp.read_bytes()).hexdigest()
    media=webp_module.MediaSources(api,work/'webp-cache')
    assert media.registerfile(str(webp.resolve()),key)['status']=='registered'
    media.activate(key);decoded=api.frames(key)
    assert decoded['ends']==[80,200] and decoded['duration']==200,decoded
    media.unregister(key)
    # Tall/wide originals are center-cropped to Kakao's 1:3 / 3:1 thumbnail.
    # Use synthetic GIF and WebP data only; test scaled previews and actual frames.
    cover_cases=0
    for kind in ('GIF','WEBP'):
        for size in ((82,300),(210,57)):
            frames=[]
            for phase in (0,1):
                image=Image.new('RGB',size,'white');draw=ImageDraw.Draw(image)
                draw.rectangle((5,10,size[0]//2,size[1]-20),fill='red')
                draw.ellipse((size[0]//3,30,size[0]-5,size[1]-5),fill='blue')
                draw.line((0,phase*10,size[0]-1,size[1]-1),fill='black',width=5);frames.append(image)
            path=work/f'cover-{size[0]}-{kind.lower()}.{kind.lower()}'
            frames[0].save(path,format=kind,save_all=True,append_images=frames[1:],duration=[80,120],loop=0,lossless=True)
            key=hashlib.sha256(path.read_bytes()).hexdigest()
            assert media.registerfile(str(path.resolve()),key)['status']=='registered'
            media.activate(key);assert api.frames(key)['duration']==200
            crop=(0,27,82,273) if size[0]==82 else (19,0,190,57)
            expected={'x':crop[0],'y':crop[1],'w':crop[2]-crop[0],'h':crop[3]-crop[1]}
            with Image.open(path) as image:first=image.convert('RGB')
            for scale in (1,1.5,2,3,4):
                preview=work/f'cover-{kind}-{size[0]}-{scale}.png'
                first.crop(crop).resize((round(expected['w']*scale),round(expected['h']*scale)),Image.Resampling.BICUBIC).save(preview)
                matched=api.probefile(str(preview));assert matched=={'id':key,'crop':expected},matched
                cover_cases+=1
            # Identical cropped first frames must not be chosen arbitrarily.
            duplicate=key+'-duplicate'
            if kind=='GIF':api.registerfile(str(path.resolve()),duplicate)
            else:api.registerpreview(str(media.webp[key]['folder']/'preview.png'),duplicate,{'frames':2,'format':'WEBP'})
            assert api.probefile(str(preview)) is None
            api.unregister(duplicate);media.unregister(key)
    benchmark=api.benchmark(str(work/'preview-0-800-3.png'))
    # Identical first frames from separate animations are ambiguous; never
    # choose an arbitrary ID based on registration order or a stale cache.
    assert api.registerfile(str(work/'source-0.gif'),'duplicate')['status']=='registered'
    assert api.probefile(str(work/'preview-0-300-3.png')) is None
    assert not errors,errors
    (work/'after.json').write_text(json.dumps({'cases':results,'transparentCases':12,'coverCases':cover_cases,'benchmark':benchmark,'snapshot':api.snapshot()},indent=2),encoding='utf8')
    api.stop();script.unload();assert all(x['match']==f"fixture-{x['source']}" for x in results),'scaled preview misses'
    print(f'Native GDI/Frida: {len(results)+12} scaled previews, {cover_cases} cropped GIF/WebP previews passed; unrelated/ambiguous rejected; {benchmark}')
finally:
    if session:session.detach()
    if process.poll() is None:process.terminate()
    process.wait(timeout=5)
