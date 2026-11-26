// Experimental, bounded in-process rendering bridge for the inspected GIF.
// The launcher validates the target process and architecture. No executable files are changed.
'use strict';
const app = Process.getModuleByName('KakaoTalk.exe');
if (!['ia32','x64'].includes(Process.arch)) throw Error('Unsupported architecture');
const WIN_ABI=Process.pointerSize===8?'win64':'stdcall';
const BITMAP_DATA_SIZE=16+2*Process.pointerSize;
const BITMAP_SIZE=Process.pointerSize===8?32:24;
const PROPERTY_VALUE_OFFSET=Process.pointerSize===8?16:12;
const PROPERTY_SIZE=PROPERTY_VALUE_OFFSET+Process.pointerSize;
const gdip = Process.getModuleByName('gdiplus.dll');
const gdi = Process.getModuleByName('gdi32.dll');
const user = Process.getModuleByName('user32.dll');
const fn = (m,n,r,a) => new NativeFunction(m.getExportByName(n),r,a,WIN_ABI);
const getWidth=fn(gdip,'GdipGetImageWidth','int',['pointer','pointer']);
const getHeight=fn(gdip,'GdipGetImageHeight','int',['pointer','pointer']);
const loadFile=fn(gdip,'GdipLoadImageFromFile','int',['pointer','pointer']);
const rawFormat=fn(gdip,'GdipGetImageRawFormat','int',['pointer','pointer']);
const dimCount=fn(gdip,'GdipImageGetFrameDimensionsCount','int',['pointer','pointer']);
const dimList=fn(gdip,'GdipImageGetFrameDimensionsList','int',['pointer','pointer','uint']);
const countFrames=fn(gdip,'GdipImageGetFrameCount','int',['pointer','pointer','pointer']);
const clone=fn(gdip,'GdipCloneImage','int',['pointer','pointer']);
const selectFrame=fn(gdip,'GdipImageSelectActiveFrame','int',['pointer','pointer','uint']);
const makeBitmap=fn(gdip,'GdipCreateHBITMAPFromBitmap','int',['pointer','pointer','uint']);
const fromBitmap=fn(gdip,'GdipCreateBitmapFromHBITMAP','int',['pointer','pointer','pointer']);
const lock=fn(gdip,'GdipBitmapLockBits','int',['pointer','pointer','uint','int','pointer']);
const unlock=fn(gdip,'GdipBitmapUnlockBits','int',['pointer','pointer']);
const dispose=fn(gdip,'GdipDisposeImage','int',['pointer']);
const getPropertySize=fn(gdip,'GdipGetPropertyItemSize','int',['pointer','uint','pointer']);
const getProperty=fn(gdip,'GdipGetPropertyItem','int',['pointer','uint','uint','pointer']);
const createDC=fn(gdi,'CreateCompatibleDC','pointer',['pointer']);
const selectObject=fn(gdi,'SelectObject','pointer',['pointer','pointer']);
const deleteObject=fn(gdi,'DeleteObject','int',['pointer']);
const deleteDC=fn(gdi,'DeleteDC','int',['pointer']);
const currentObject=fn(gdi,'GetCurrentObject','pointer',['pointer','uint']);
const getObject=fn(gdi,'GetObjectW','int',['pointer','int','pointer']);
const invalidate=fn(user,'InvalidateRect','int',['pointer','pointer','int']);
const redraw=fn(user,'RedrawWindow','int',['pointer','pointer','pointer','uint']);
const windowPid=fn(user,'GetWindowThreadProcessId','uint',['pointer','pointer']);
const isIconic=fn(user,'IsIconic','int',['pointer']);
const isVisible=fn(user,'IsWindowVisible','int',['pointer']);
const hwnd=ptr(BRIDGE_CONFIG.hwnd), pidOut=Memory.alloc(Process.pointerSize);
const uiThread=windowPid(hwnd,pidOut);
if (!uiThread || pidOut.readU32()!==Process.id) throw Error('Test chat window changed');
const listeners=[], frames=[], cache=new Map();
const stats={captured:false,candidates:0,matches:0,replacements:0,errors:[],frameUse:{},draws:{},replacementCalls:{},comparisons:[]};
const references=[];
let busy=false, stopped=false, cleaned=false, baseline=null, started=0, duration=0;
let imageWidth=0,imageHeight=0,lastMatch=0;
function pixels(image) {
  const rect=Memory.alloc(16);rect.writeS32(0);rect.add(4).writeS32(0);rect.add(8).writeS32(imageWidth);rect.add(12).writeS32(imageHeight);
  const data=Memory.alloc(BITMAP_DATA_SIZE);
  if(lock(image,rect,1,0x26200a,data)!==0) return null;
  try {
    const stride=data.add(8).readS32(), scan=data.add(16).readPointer();
    if(Math.abs(stride)<imageWidth*4 || Math.abs(stride)>65536) return null;
    const out=new Uint8Array(imageWidth*imageHeight*3);
    for(let y=0;y<imageHeight;y++) {
      const row=new Uint8Array(scan.add(y*stride).readByteArray(imageWidth*4));
      for(let x=0;x<imageWidth;x++) for(let c=0;c<3;c++) out[(y*imageWidth+x)*3+c]=row[x*4+c];
    }
    return out;
  } finally {unlock(image,data);}
}
function capture(image) {
  const n=Memory.alloc(Process.pointerSize),w=Memory.alloc(Process.pointerSize),h=Memory.alloc(Process.pointerSize);
  // GUID from the installed Windows SDK gdiplusimaging.h (ImageFormatGIF).
  const format=Memory.alloc(16);
  if(rawFormat(image,format)!==0 || Array.from(new Uint8Array(format.readByteArray(16))).join(',')!=='176,60,107,185,40,7,211,17,157,123,0,0,248,30,243,46')return false;
  if(getWidth(image,w)!==0 || getHeight(image,h)!==0) return false;
  if(dimCount(image,n)!==0 || n.readU32()!==1) return;
  const guid=Memory.alloc(16);
  if(dimList(image,guid,1)!==0 || countFrames(image,guid,n)!==0 || n.readU32()<2) return false;
  const width=w.readU32(),height=h.readU32(),count=n.readU32();
  // Include decoded images, reference pixels and native bitmap overhead in budget.
  if(width<9 || height<9 || width>1024 || height>1024 || count>256 || width*height*count*16>64*1024*1024) {
    stats.rejected='Source exceeds dimension/frame/memory budget';return false;
  }
  imageWidth=width;imageHeight=height;
  const out=Memory.alloc(Process.pointerSize);
  if(clone(image,out)!==0) throw Error('Could not clone source GIF');
  const copy=out.readPointer();
  try {
    const delays=Array(count).fill(100);
    if(getPropertySize(copy,0x5100,n)===0 && n.readU32()>=PROPERTY_SIZE+count*4 && n.readU32()<4096) {
      const size=n.readU32(), prop=Memory.alloc(size);
      if(getProperty(copy,0x5100,size,prop)===0 && prop.add(4).readU32()>=count*4 && prop.add(8).readU16()===4) {
        const values=prop.add(PROPERTY_VALUE_OFFSET).readPointer();
        if(values.compare(prop)<0 || values.add(count*4).compare(prop.add(size))>0) throw Error('Invalid delay property bounds');
        for(let i=0;i<count;i++) delays[i]=Math.max(20,Math.min(60000,values.add(i*4).readU32()*10));
      }
    }
    for(let i=0;i<count;i++) {
      if(selectFrame(copy,guid,i)!==0) throw Error('Frame selection failed');
      references.push(pixels(copy));
      if(i===0) baseline=references[0];
      if(makeBitmap(copy,out,0xffffffff)!==0) throw Error('Bitmap creation failed');
      const bitmap=out.readPointer(), dc=createDC(ptr(0));
      if(dc.isNull()) {deleteObject(bitmap);throw Error('DC creation failed');}
      const old=selectObject(dc,bitmap);
      frames.push({bitmap,dc,old,end:duration+=delays[i]});
    }
    if(references.some(ref=>!ref)) throw Error('Missing frame reference pixels');
    stats.captured=true;started=Date.now();
    stats.source={frames:count,width,height,delays};
    send({status:'source-captured',...stats.source});
    return true;
  } finally {dispose(copy);}
}
listeners.push(Interceptor.attach(gdip.getExportByName('GdipImageSelectActiveFrame'),{
  onEnter(args){
    if(busy || stats.captured || stopped || this.threadId!==uiThread)return;
    busy=true;
    try{capture(args[0]);}catch(e){stats.errors.push(String(e));stopped=true;}
    finally{busy=false;}
  }
}));
function match(bitmap) {
  const key=bitmap.toString();if(cache.has(key))return cache.get(key);
  const info=Memory.alloc(BITMAP_SIZE);
  if(!getObject(bitmap,BITMAP_SIZE,info) || info.add(4).readS32()!==imageWidth || Math.abs(info.add(8).readS32())!==imageHeight)return false;
  stats.candidates++;
  const out=Memory.alloc(Process.pointerSize);let yes=false;
  if(fromBitmap(bitmap,ptr(0),out)===0) {
    const image=out.readPointer();
    try{
      const actual=pixels(image);
      let best=Infinity,badFraction=1;
      if(actual) for(const ref of references) {
        if(!ref)continue;
        let sum=0,bad=0,total=0;
        // Rounded thumbnail corners are not part of the content comparison.
        for(let y=4;y<imageHeight-4;y++)for(let x=4;x<imageWidth-4;x++)for(let c=0;c<3;c++) {
          const i=(y*imageWidth+x)*3+c,d=Math.abs(actual[i]-ref[i]);sum+=d;bad+=d>24;total++;
        }
        if(sum/total<best){best=sum/total;badFraction=bad/total;}
      }
      yes=best===0;
      if(stats.comparisons.length<24)stats.comparisons.push({meanError:best,badFraction,matched:yes});
    }
    finally{dispose(image);}
  }
  if(cache.size<2048)cache.set(key,yes);
  if(yes){stats.matches++;send({status:'matching-bitmap',bitmap:key});}
  return yes;
}
for(const [owner,name] of [[gdi,'BitBlt'],[gdi,'StretchBlt'],[Process.findModuleByName('msimg32.dll'),'AlphaBlend']]) {
  if(!owner)continue;
  const address=owner.findExportByName(name);if(!address)continue;
  listeners.push(Interceptor.attach(address,{onEnter(args){
    if(busy || stopped || !stats.captured || this.threadId!==uiThread)return;
    if(this.returnAddress.compare(app.base)<0 || this.returnAddress.compare(app.base.add(app.size))>=0)return;
    // Observed chat thumbnail AlphaBlend call; exclude the image viewer.
    if(name!=='AlphaBlend' || !this.returnAddress.equals(app.base.add(0x13ab46e)))return;
    busy=true;
    try{
      const bitmap=currentObject(args[5],7);
      const info=Memory.alloc(BITMAP_SIZE);
      if(!bitmap.isNull() && getObject(bitmap,BITMAP_SIZE,info)) {
        const key=[name,this.returnAddress.sub(app.base),args[3].toInt32(),args[4].toInt32(),info.add(4).readS32(),info.add(8).readS32(),args[6].toInt32(),args[7].toInt32()].join(':');
        if(Object.keys(stats.draws).length<120 || stats.draws[key])stats.draws[key]=(stats.draws[key]||0)+1;
      }
      if(args[6].toInt32()!==0 || args[7].toInt32()!==0)return;
      if(args[8].toInt32()!==imageWidth || args[9].toInt32()!==imageHeight)return;
      if(bitmap.isNull() || !match(bitmap))return;
      lastMatch=Date.now();
      const elapsed=(Date.now()-started)%duration;
      const i=frames.findIndex(f=>elapsed<f.end);
      args[5]=frames[i].dc;stats.replacements++;stats.frameUse[i]=(stats.frameUse[i]||0)+1;
      const call=this.returnAddress.sub(app.base).toString();stats.replacementCalls[call]=(stats.replacementCalls[call]||0)+1;
    }catch(e){stats.errors.push(String(e));stopped=true;}
    finally{busy=false;}
  }}));
}
listeners.push(Interceptor.attach(gdi.getExportByName('DeleteObject'),{onEnter(args){cache.delete(args[0].toString());}}));
let timer=null;
function tick() {
  if(stopped)return;
  if(windowPid(hwnd,pidOut)!==uiThread || pidOut.readU32()!==Process.id) {
    stopped=true;stats.stoppedReason='Chat window closed or replaced';return;
  }
  let next=500;
  if(stats.captured && isVisible(hwnd) && !isIconic(hwnd)) {
    redraw(hwnd,ptr(0),ptr(0),0x81);
    if(Date.now()-lastMatch<1000) {
      const elapsed=(Date.now()-started)%duration;
      next=Math.max(20,frames.find(f=>elapsed<f.end).end-elapsed);
    }
  }
  timer=setTimeout(tick,next);
}
timer=setTimeout(tick,100);
rpc.exports={snapshot(){return stats;},stop(){
  if(cleaned)return;cleaned=true;
  stopped=true;clearTimeout(timer);for(const l of listeners)l.detach();
  for(const f of frames){selectObject(f.dc,f.old);deleteDC(f.dc);deleteObject(f.bitmap);}
  redraw(hwnd,ptr(0),ptr(0),0x81);
}};
if(BRIDGE_CONFIG.sourceFile) {
  busy=true;
  const out=Memory.alloc(Process.pointerSize);
  try {
    const status=loadFile(Memory.allocUtf16String(BRIDGE_CONFIG.sourceFile),out);
    if(status!==0)throw Error('GIF load failed: '+status);
    const original=out.readPointer();
    try{if(!capture(original))throw Error(stats.rejected||'Not a supported animated source');}
    finally{dispose(original);}
  }catch(e){stats.errors.push(String(e));stopped=true;}
  finally{busy=false;}
}
send({status:stopped?'bridge-rejected':'bridge-armed',waiting:stats.captured?'Matching visible thumbnail':'Open a GIF once, then close its viewer'});
