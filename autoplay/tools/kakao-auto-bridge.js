// Included after the native GDI bindings from kakao-bitmap-bridge.js.
// Source indexing is separate from decoded frame residency.
const hwnd=ptr(BRIDGE_CONFIG.hwnd),pidOut=Memory.alloc(Process.pointerSize);
const uiThread=windowPid(hwnd,pidOut);
let watchedWindows=[hwnd];
if(!uiThread || pidOut.readU32()!==Process.id)throw Error('Chat window changed');
const sources=new Map(),buckets=new Map(),cache=new Map(),pending=new Set(),observed=new Set(),listeners=[];
const stats={rendererVersion:10,captured:false,registered:0,active:0,maxActive:0,replacements:0,matches:0,ambiguous:0,matchErrors:0,errors:[],rejected:0,bySource:{},comparisons:[],sizes:{}};
const MAX_REFS=32*1024*1024,MAX_DECODED=128*1024*1024;
const SIGNATURE_SIZE=96,MAX_MATCH_CACHE=128;
const MAX_BITMAP_CACHE=32*1024*1024;
let bitmapCacheBytes=0;
const flushGdi=fn(gdi,'GdiFlush','int',[]);
const MIN_REFRESH_MS=34;
const refreshClient=fn(user,'GetClientRect','int',['pointer','pointer']);
const refreshMap=fn(user,'MapWindowPoints','int',['pointer','pointer','pointer','uint']);
const refreshChildren=fn(user,'EnumChildWindows','int',['pointer','pointer','pointer']);
const refreshClass=fn(user,'GetClassNameW','int',['pointer','pointer','int']);
stats.refreshPolicy='conversation-only-no-children-no-erase';stats.refreshRequests=0;stats.refreshSkipped=0;
function clipRefreshRect(rect,width,cutoff){
  const left=Math.max(0,rect.left),top=Math.max(0,rect.top),right=Math.min(width,rect.right),bottom=Math.min(cutoff,rect.bottom);
  return right>left&&bottom>top?{left,top,right,bottom}:null;
}
function conversationCutoff(width,height,children){
  const editors=children.filter(c=>/^RICHEDIT/i.test(c.cls)&&c.rect.right-c.rect.left>=width/2&&c.rect.bottom>height/2&&c.rect.top>0);
  return editors.length?Math.max(0,Math.min(...editors.map(c=>c.rect.top))-1):null;
}
function clientInRoot(window,root){
  const rect=Memory.alloc(16);if(!refreshClient(window,rect))return null;
  refreshMap(window,root,rect,2);
  return {left:rect.readS32(),top:rect.add(4).readS32(),right:rect.add(8).readS32(),bottom:rect.add(12).readS32()};
}
function refreshConversation(root){
  try{
    const client=clientInRoot(root,root);if(!client)return;
    const children=[],name=Memory.alloc(256);let visited=0,overflow=false;
    const visit=new NativeCallback((window,_)=>{
      if(++visited>128){overflow=true;return 0;}if(!isVisible(window))return 1;
      const rect=clientInRoot(window,root);if(!rect)return 1;
      refreshClass(window,name,128);children.push({window,rect,cls:name.readUtf16String()});return 1;
    },'int',['pointer','pointer'],WIN_ABI);
    refreshChildren(root,visit,ptr(0));
    const cutoff=overflow?null:conversationCutoff(client.right,client.bottom,children);
    if(cutoff===null||cutoff<=0){stats.refreshSkipped++;return;}
    for(const child of [{window:root,rect:client,cls:''},...children]){
      if(/^RICHEDIT/i.test(child.cls))continue;
      const intersection=clipRefreshRect(child.rect,client.right,cutoff);if(!intersection)continue;
      const rect=Memory.alloc(16);rect.writeS32(intersection.left);rect.add(4).writeS32(intersection.top);rect.add(8).writeS32(intersection.right);rect.add(12).writeS32(intersection.bottom);
      // Every target gets an explicit clipped client region. Do not propagate
      // invalidation to editor descendants or request background erasure.
      refreshMap(root,child.window,rect,2);redraw(child.window,rect,ptr(0),0x61);stats.refreshRequests++;
    }
  }catch(_){stats.refreshSkipped++;}
}
stats.architecture=Process.arch;stats.pointerSize=Process.pointerSize;
stats.performancePatch='4k-dib-cache';stats.maxRefreshFps=1000/MIN_REFRESH_MS;
stats.performance={calls:0,matchMs:0,fromBitmapMs:0,normalizeMs:0,chooseMs:0,bitmapCacheHits:0};
// PERF_NATIVE_BEGIN: arrays passed to these helpers are immutable snapshots.
const pixelModule=new CModule(`
extern int memcmp(const void *a,const void *b,${Process.pointerSize===8?'unsigned long long':'unsigned int'} n);
void copy_crop(const unsigned char *scan, int stride, int w, int h, unsigned char *out) {
  int x,y,c; unsigned int k=0;
  for(y=4;y<h-4;y++) {
    const unsigned char *row=scan+y*stride+16;
    for(x=0;x<w-8;x++)for(c=0;c<3;c++)out[k++]=row[x*4+c];
  }
}
int equal_bytes(const unsigned char *a,const unsigned char *b,unsigned int n) {
  return memcmp(a,b,n)==0;
}
unsigned int hash_bytes(const unsigned char *a,unsigned int n) {
  unsigned int i,h=2166136261U;for(i=0;i<n;i++)h=(h^a[i])*16777619U;return h;
}
`,{memcmp:Module.load('msvcrt.dll').getExportByName('memcmp')});
const copyCrop=new NativeFunction(pixelModule.copy_crop,'void',['pointer','int','int','int','pointer']);
const equalBytes=new NativeFunction(pixelModule.equal_bytes,'int',['pointer','pointer','uint']);
const hashBytes=new NativeFunction(pixelModule.hash_bytes,'uint',['pointer','uint']);
const nativePixels=new WeakMap();
function nativeBytes(bytes){
  let buffer=nativePixels.get(bytes);
  if(!buffer){buffer=Memory.alloc(Math.max(1,bytes.length));if(bytes.length)buffer.writeByteArray(bytes);nativePixels.set(bytes,buffer);}
  return buffer;
}
function cropPixels(scan,stride,w,h){
  if(w<9||h<9||w>4096||h>4096||Math.abs(stride)<w*4||Math.abs(stride)>65536)throw Error('Invalid pixel bounds');
  const size=(w-8)*(h-8)*3,buffer=Memory.alloc(size);
  copyCrop(scan,stride,w,h,buffer);
  const bytes=new Uint8Array(buffer.readByteArray(size));nativePixels.set(bytes,buffer);return bytes;
}
function fingerprint(bytes){return hashBytes(nativeBytes(bytes),bytes.length).toString(16);}
function same(a,b){return a.length===b.length&&equalBytes(nativeBytes(a),nativeBytes(b),a.length)!==0;}
// PERF_NATIVE_END
const imageThumbnail=fn(gdip,'GdipGetImageThumbnail','int',['pointer','uint','uint','pointer','pointer','pointer']);
const cloneArea=fn(gdip,'GdipCloneBitmapAreaI','int',['int','int','int','int','int','pointer','pointer']);
let refBytes=0,decodedBytes=0,busy=false,stopped=false,cleaned=false,timer=null;
function pixels(image,w,h){
  const rect=Memory.alloc(16),data=Memory.alloc(BITMAP_DATA_SIZE);
  rect.writeS32(0);rect.add(4).writeS32(0);rect.add(8).writeS32(w);rect.add(12).writeS32(h);
  if(lock(image,rect,1,0x26200a,data)!==0)throw Error('Pixel lock failed');
  try{
    const stride=data.add(8).readS32(),scan=data.add(16).readPointer();
    if(Math.abs(stride)<w*4 || Math.abs(stride)>65536)throw Error('Invalid stride');
    return cropPixels(scan,stride,w,h);
  }finally{unlock(image,data);}
}
function normalizedPixels(image,variants=false){
  const out=Memory.alloc(Process.pointerSize);if(imageThumbnail(image,SIGNATURE_SIZE,SIGNATURE_SIZE,out,ptr(0),ptr(0))!==0)throw Error('Thumbnail normalization failed');
  const thumb=out.readPointer();
  try{
    if(!variants)return [pixels(thumb,SIGNATURE_SIZE,SIGNATURE_SIZE)];
    const rect=Memory.alloc(16),data=Memory.alloc(BITMAP_DATA_SIZE),size=SIGNATURE_SIZE;
    rect.writeS32(0);rect.add(4).writeS32(0);rect.add(8).writeS32(size);rect.add(12).writeS32(size);
    if(lock(thumb,rect,1,0x26200a,data)!==0)throw Error('Signature pixel lock failed');
    try{
      const stride=data.add(8).readS32(),scan=data.add(16).readPointer();
      if(Math.abs(stride)<size*4||Math.abs(stride)>65536)throw Error('Signature stride invalid');
      const raw=new Uint8Array((size-8)*(size-8)*3),white=new Uint8Array(raw.length),black=new Uint8Array(raw.length);let k=0;
      for(let y=4;y<size-4;y++){const row=new Uint8Array(scan.add(y*stride+16).readByteArray((size-8)*4));for(let x=0;x<size-8;x++)for(let c=0;c<3;c++){
        const value=row[x*4+c],alpha=row[x*4+3];raw[k]=value;black[k]=Math.round(value*alpha/255);white[k]=black[k]+255-alpha;k++;
      }}
      const result=[raw];for(const value of [white,black])if(!result.some(old=>same(old,value)))result.push(value);return result;
    }finally{unlock(thumb,data);}
  }finally{dispose(thumb);}
}
function compatibleAspect(w,h,sw,sh){return w>=9&&h>=9&&w<=4096&&h<=4096&&Math.abs(w*sh-h*sw)<=Math.max(w*sh,h*sw)*.015+Math.max(w,h,sw,sh);}
function mapSourceRect(w,h,x,y,width,height,frameW,frameH){
  if(w<=0||h<=0||frameW<=0||frameH<=0||x<0||y<0||width<=0||height<=0||x+width>w||y+height>h)return null;
  const left=Math.min(frameW-1,Math.round(x*frameW/w)),top=Math.min(frameH-1,Math.round(y*frameH/h));
  const right=Math.min(frameW,Math.max(left+1,Math.round((x+width)*frameW/w))),bottom=Math.min(frameH,Math.max(top+1,Math.round((y+height)*frameH/h)));
  return {x:left,y:top,w:right-left,h:bottom-top};
}
// Kakao clamps very tall/wide photo thumbnails to a 1:3 / 3:1 center crop.
// Build this extra reference once at registration, never while painting.
function coverRect(w,h){
  let cw=w,ch=h;
  if(h>3*w)ch=3*w;else if(w>3*h)cw=3*h;else return null;
  if(cw*ch/(w*h)<.65)return null;
  return {x:Math.floor((w-cw)/2),y:Math.floor((h-ch)/2),w:cw,h:ch};
}
function sourceCandidates(w,h){
  const result=[];
  for(const s of sources.values()){
    if(compatibleAspect(w,h,s.w,s.h))result.push(s);
    else if(s.cover&&compatibleAspect(w,h,s.cover.crop.w,s.cover.crop.h))result.push(s.cover);
  }
  return result;
}
function matchResult(candidate){return {id:candidate.id,crop:candidate.crop||null};}
function mapMatchedRect(w,h,x,y,width,height,s,matched){
  const c=matched.crop;if(!c)return mapSourceRect(w,h,x,y,width,height,s.w,s.h);
  const r=mapSourceRect(w,h,x,y,width,height,c.w,c.h);
  return r?{x:r.x+c.x,y:r.y+c.y,w:r.w,h:r.h}:null;
}
function coarsePixels(bytes,w,h){
  const width=w-8,height=h-8,grid=12,sums=new Float64Array(grid*grid*3),counts=new Uint32Array(grid*grid);
  for(let y=0;y<height;y++)for(let x=0;x<width;x++){
    const cell=Math.floor(y*grid/height)*grid+Math.floor(x*grid/width),offset=(y*width+x)*3;
    counts[cell]++;for(let c=0;c<3;c++)sums[cell*3+c]+=bytes[offset+c];
  }
  for(let i=0;i<sums.length;i++)sums[i]/=counts[Math.floor(i/3)]||1;
  return sums;
}
function describe(image){
  const out=Memory.alloc(Process.pointerSize),guid=Memory.alloc(16),fmt=Memory.alloc(16);
  if(rawFormat(image,fmt)!==0 || fmt.readU32()!==0xb96b3cb0)return null;
  if(getWidth(image,out)!==0)return null;const w=out.readU32();
  if(getHeight(image,out)!==0)return null;const h=out.readU32();
  if(w<9||h<9||w>1024||h>1024)return null;
  if(dimCount(image,out)!==0||out.readU32()!==1||dimList(image,guid,1)!==0||countFrames(image,guid,out)!==0)return null;
  const n=out.readU32();if(n<2||n>256||w*h*n*8>64*1024*1024)return null;
  return {w,h,n,guid};
}
function open(path,action){
  const out=Memory.alloc(Process.pointerSize),status=loadFile(Memory.allocUtf16String(path),out);
  if(status!==0)throw Error('Image load failed: '+status);
  const image=out.readPointer();try{return action(image);}finally{dispose(image);}
}
function release(s){
  if(!s.frames)return;
  for(const f of s.frames){selectObject(f.dc,f.old);deleteDC(f.dc);deleteObject(f.bitmap);}
  decodedBytes-=s.cost;s.frames=null;stats.active--;s.report.active=false;
}
function unregister(id){
  const s=sources.get(id);if(!s)return;
  release(s);refBytes-=s.referenceCost;sources.delete(id);pending.delete(id);
  for(const key of s.bucketKeys){const bucket=buckets.get(key).filter(x=>x!==id);if(bucket.length)buckets.set(key,bucket);else buckets.delete(key);}
  delete stats.bySource[id];clearMatches();stats.registered=sources.size;
}
function register(path,id,external=null){
  if(sources.has(id))return {status:'known'};
  if(sources.size>=512)return {status:'limit'};
  busy=true;
  try{return open(path,image=>{
    let d=describe(image);
    if(external){
      const out=Memory.alloc(Process.pointerSize);getWidth(image,out);const w=out.readU32();getHeight(image,out);const h=out.readU32();
      d={w,h,n:external.frames};
      if(w<9||h<9||w>1024||h>1024||d.n<2||d.n>900||w*h*d.n*4>64*1024*1024)d=null;
    }
    if(!d){stats.rejected++;return {status:'unsupported'};}
    if(!external&&selectFrame(image,d.guid,0)!==0)throw Error('Cannot select first frame');
    const refs=normalizedPixels(image,true),ref=refs[0];
    let referenceCost=refs.reduce((n,value)=>n+value.length,0),cover=null;
    const crop=coverRect(d.w,d.h);
    if(crop){
      const out=Memory.alloc(Process.pointerSize);
      if(cloneArea(crop.x,crop.y,crop.w,crop.h,0x26200a,image,out)===0){
        const cut=out.readPointer();
        try{const values=normalizedPixels(cut,true);cover={id,crop,refs:values,coarse:values.map(value=>coarsePixels(value,SIGNATURE_SIZE,SIGNATURE_SIZE))};referenceCost+=values.reduce((n,value)=>n+value.length,0);}
        finally{dispose(cut);}
      }
    }
    if(refBytes+referenceCost>MAX_REFS)return {status:'limit'};
    const bucketKeys=[...new Set(refs.map(fingerprint))];
    const report={format:external?external.format:'GIF',width:d.w,height:d.h,frames:d.n,active:false,replacements:0,frameUse:{},loops:0};
    sources.set(id,{id,path,external:!!external,w:d.w,h:d.h,n:d.n,ref,refs,cover,coarse:refs.map(value=>coarsePixels(value,SIGNATURE_SIZE,SIGNATURE_SIZE)),referenceCost,bucketKeys,frames:null,lastSeen:0,report});
    for(const key of bucketKeys){if(!buckets.has(key))buckets.set(key,[]);buckets.get(key).push(id);}
    refBytes+=referenceCost;clearMatches();stats.registered=sources.size;stats.bySource[id]=report;
    return {status:'registered',width:d.w,height:d.h,frames:d.n};
  });}catch(e){return {status:'error',reason:String(e)};}finally{busy=false;}
}
function activate(id,pack=null){
  const s=sources.get(id);pending.delete(id);if(!s||s.frames)return;
  if(s.external&&!pack)return;
  s.lastSeen=Date.now();
  const cost=s.w*s.h*s.n*(s.external?4:8);
  for(const old of [...sources.values()].filter(x=>x.frames&&Date.now()-x.lastSeen>3000).sort((a,b)=>a.lastSeen-b.lastSeen)){
    if(decodedBytes+cost<=MAX_DECODED&&stats.active<8)break;release(old);
  }
  if(decodedBytes+cost>MAX_DECODED||stats.active>=8)return;
  busy=true;const built=[];
  try{
    if(s.external){
      if(pack.paths.length!==s.n||pack.delays.length!==s.n)throw Error('Frame pack mismatch');
      const out=Memory.alloc(Process.pointerSize);let duration=0;
      for(let i=0;i<s.n;i++)open(pack.paths[i],image=>{
        getWidth(image,out);if(out.readU32()!==s.w)throw Error('Frame width mismatch');
        getHeight(image,out);if(out.readU32()!==s.h)throw Error('Frame height mismatch');
        if(i===0&&!same(normalizedPixels(image)[0],s.ref))throw Error('Preview changed');
        if(makeBitmap(image,out,0xffffffff)!==0)throw Error('Frame bitmap failed');
        const bitmap=out.readPointer(),dc=createDC(ptr(0));
        if(dc.isNull()){deleteObject(bitmap);throw Error('DC allocation failed');}
        const old=selectObject(dc,bitmap);
        if(old.isNull()||old.equals(ptr(-1))){deleteDC(dc);deleteObject(bitmap);throw Error('DC selection failed');}
        if(!Number.isFinite(pack.delays[i])||pack.delays[i]<20||pack.delays[i]>600000000)throw Error('Invalid frame delay');
        built.push({bitmap,dc,old,end:duration+=pack.delays[i]});
      });
      s.duration=duration;
    }else open(s.path,image=>{
    const d=describe(image);if(!d||d.w!==s.w||d.h!==s.h||d.n!==s.n)throw Error('Source changed');
    if(selectFrame(image,d.guid,0)!==0||!same(normalizedPixels(image)[0],s.ref))throw Error('Source pixels changed');
    const out=Memory.alloc(Process.pointerSize),delays=Array(s.n).fill(100);
    if(getPropertySize(image,0x5100,out)===0&&out.readU32()>=PROPERTY_SIZE+s.n*4&&out.readU32()<4096){
      const size=out.readU32(),prop=Memory.alloc(size);
      if(getProperty(image,0x5100,size,prop)===0&&prop.add(8).readU16()===4&&prop.add(4).readU32()>=s.n*4){
        const values=prop.add(PROPERTY_VALUE_OFFSET).readPointer();
        if(values.compare(prop)<0||values.add(s.n*4).compare(prop.add(size))>0)throw Error('Invalid delay bounds');
        for(let i=0;i<s.n;i++)delays[i]=Math.max(20,Math.min(60000,values.add(i*4).readU32()*10));
      }
    }
    let duration=0;
    for(let i=0;i<s.n;i++){
      if(selectFrame(image,d.guid,i)!==0||makeBitmap(image,out,0xffffffff)!==0)throw Error('Frame decode failed');
      const bitmap=out.readPointer(),dc=createDC(ptr(0));
      if(dc.isNull()){deleteObject(bitmap);throw Error('DC allocation failed');}
      const old=selectObject(dc,bitmap);
      if(old.isNull()||old.equals(ptr(-1))){deleteDC(dc);deleteObject(bitmap);throw Error('DC selection failed');}
      built.push({bitmap,dc,old,end:duration+=delays[i]});
    }
    s.duration=duration;
  });
    s.frames=built;s.started=Date.now();s.lastCycle=0;s.cost=cost;
    decodedBytes+=cost;stats.active++;stats.maxActive=Math.max(stats.maxActive,stats.active);stats.captured=true;s.report.active=true;
  }catch(e){
    for(const f of built){selectObject(f.dc,f.old);deleteDC(f.dc);deleteObject(f.bitmap);}
    s.report.error=String(e);s.failed=true;
  }finally{busy=false;}
}
function chooseSource(actual,w,h){
  const candidates=sourceCandidates(w,h);
  const ids=new Set(buckets.get(fingerprint(actual))||[]);
  const exact=candidates.filter(s=>(s.crop||ids.has(s.id))&&s.refs.some(ref=>same(actual,ref)));
  if(exact.length===1){stats.matches++;return matchResult(exact[0]);}
  if(exact.length>1){stats.ambiguous++;return null;}
  const ranked=[],actualCoarse=coarsePixels(actual,SIGNATURE_SIZE,SIGNATURE_SIZE);
  for(const s of candidates){
    let best=null;
    for(let variant=0;variant<s.refs.length;variant++){
      const ref=s.refs[variant];let error=0,total=0,bad=0,min=255,max=0;
      const step=Math.max(1,Math.floor(actual.length/4096));
      for(let i=0;i<actual.length;i+=step){const diff=Math.abs(actual[i]-ref[i]);error+=diff;bad+=diff>24;total++;min=Math.min(min,ref[i]);max=Math.max(max,ref[i]);}
      let coarse=0,coarseMax=0;
      for(let i=0;i<actualCoarse.length;i++){const delta=Math.abs(actualCoarse[i]-s.coarse[variant][i]);coarse+=delta;coarseMax=Math.max(coarseMax,delta);}
      const score={id:s.id,crop:s.crop||null,error:error/total,bad:bad/total,range:max-min,coarse:coarse/actualCoarse.length,coarseMax};
      if(!best||score.error<best.error)best=score;
    }
    if(best)ranked.push(best);
  }
  ranked.sort((a,b)=>a.error-b.error);const best=ranked[0];
  if(best&&stats.comparisons.length<40)stats.comparisons.push({w,h,...best});
  if(best&&((best.error<8&&best.bad<.08&&best.range>50&&best.coarse<5&&best.coarseMax<36&&(!ranked[1]||ranked[1].error-best.error>3))||
    (best.error<12&&best.bad<.16&&best.range>50&&best.coarse<3&&best.coarseMax<15&&ranked.slice(1).every(x=>x.coarse-best.coarse>3)))){
    stats.matches++;return matchResult(best);
  }
  return null;
}
// Compare every byte of a bounded 32-bit DIB snapshot before reusing a match.
// HBITMAP identity alone is unsafe: Kakao can repaint an existing bitmap.
// DDBs and unsupported layouts retain the normalized-pixel validation path.
function bitmapView(bitmap,w,h,info=null){
  if(!info){info=Memory.alloc(BITMAP_SIZE);if(getObject(bitmap,BITMAP_SIZE,info)!==BITMAP_SIZE)return null;}
  const stride=info.add(12).readS32(),bits=info.add(Process.pointerSize===8?24:20).readPointer();
  if(bits.isNull()||info.add(16).readU16()!==1||info.add(18).readU16()!==32||stride<w*4||stride>65536)return null;
  const size=stride*h;if(size<=0||size>MAX_BITMAP_CACHE)return null;
  if(!flushGdi())return null;
  return {bits,stride,size};
}
function forgetMatch(key){
  const old=cache.get(key);if(old&&old.bitmap)bitmapCacheBytes-=old.bitmap.size;
  cache.delete(key);
}
function clearMatches(){cache.clear();bitmapCacheBytes=0;}
function rememberMatch(key,entry,view){
  forgetMatch(key);
  while(cache.size>=MAX_MATCH_CACHE||(view&&bitmapCacheBytes+view.size>MAX_BITMAP_CACHE))forgetMatch(cache.keys().next().value);
  if(view){
    const bits=Memory.alloc(view.size);Memory.copy(bits,view.bits,view.size);
    entry.bitmap={bits,stride:view.stride,size:view.size};bitmapCacheBytes+=view.size;
  }
  cache.set(key,entry);
}
function match(bitmap,w,h,info=null){
  const started=Date.now();stats.performance.calls++;
  try{
  const key=bitmap.toString();
  if(!sourceCandidates(w,h).length)return null;
  let view=null;
  try{view=bitmapView(bitmap,w,h,info);}catch(_){/* Fall back to GDI+ on inaccessible storage. */}
  const old=cache.get(key);
  if(view&&old&&old.w===w&&old.h===h&&old.bitmap&&old.bitmap.stride===view.stride&&old.bitmap.size===view.size&&equalBytes(old.bitmap.bits,view.bits,view.size)){
    stats.performance.bitmapCacheHits++;return old.id;
  }
  const out=Memory.alloc(Process.pointerSize);let found=null;
  const fromStarted=Date.now(),fromResult=fromBitmap(bitmap,ptr(0),out);
  stats.performance.fromBitmapMs+=Date.now()-fromStarted;
  if(fromResult===0){
    const image=out.readPointer();try{
      const normalizeStarted=Date.now(),actual=normalizedPixels(image)[0];
      stats.performance.normalizeMs+=Date.now()-normalizeStarted;
      if(old&&old.w===w&&old.h===h&&same(actual,old.pixels))found=old.id;
      else{const chooseStarted=Date.now();found=chooseSource(actual,w,h);stats.performance.chooseMs+=Date.now()-chooseStarted;}
      rememberMatch(key,{w,h,pixels:actual,id:found},view);
    }catch(_){stats.matchErrors++;forgetMatch(key);}finally{dispose(image);}
  }
  return found;
  }finally{stats.performance.matchMs+=Date.now()-started;}
}
const blend=Process.getModuleByName('msimg32.dll').getExportByName('AlphaBlend');
listeners.push(Interceptor.attach(blend,{onEnter(args){
  if(busy||stopped||this.threadId!==uiThread||(this.returnAddress.compare(app.base)<0||this.returnAddress.compare(app.base.add(app.size))>=0))return;
  busy=true;
  try{
    const bitmap=currentObject(args[5],7),info=Memory.alloc(BITMAP_SIZE);
    if(bitmap.isNull()||!getObject(bitmap,BITMAP_SIZE,info))return;
    const w=info.add(4).readS32(),h=Math.abs(info.add(8).readS32());
    const size=w+'x'+h;if(Object.keys(stats.sizes).length<100||stats.sizes[size])stats.sizes[size]=(stats.sizes[size]||0)+1;
    const x=args[6].toInt32(),y=args[7].toInt32(),width=args[8].toInt32(),height=args[9].toInt32();
    if(!mapSourceRect(w,h,x,y,width,height,w,h))return;
    const matched=match(bitmap,w,h,info);if(!matched)return;const id=matched.id;
    const s=sources.get(id);s.lastSeen=Date.now();
    if(!s.frames){if(!s.failed)pending.add(id);return;}
    const age=Date.now()-s.started,elapsed=age%s.duration,i=s.frames.findIndex(f=>elapsed<f.end);
    const cycle=Math.floor(age/s.duration);if(cycle>s.lastCycle){s.report.loops+=cycle-s.lastCycle;s.lastCycle=cycle;}
    const rect=mapMatchedRect(w,h,x,y,width,height,s,matched);if(!rect||i<0)return;
    args[5]=s.frames[i].dc;args[6]=ptr(rect.x);args[7]=ptr(rect.y);args[8]=ptr(rect.w);args[9]=ptr(rect.h);
    stats.replacements++;s.report.replacements++;
    s.report.frameUse[i]=(s.report.frameUse[i]||0)+1;
  }catch(e){stats.errors.push(String(e));stopped=true;}finally{busy=false;}
}}));
listeners.push(Interceptor.attach(gdi.getExportByName('DeleteObject'),{onEnter(args){forgetMatch(args[0].toString());}}));
// Observe only GIF paths opened by this application; do not inspect message text or credentials.
const kernel=Process.getModuleByName('kernelbase.dll');
listeners.push(Interceptor.attach(kernel.getExportByName('CreateFileW'),{onEnter(args){
  this.gif=null;if(busy||stopped)return;
  try{const path=args[0].readUtf16String();if(path&&path.length<1024&&/\.(gif|webp)$/i.test(path))this.gif=path;}catch(_){}
},onLeave(result){if(this.gif&&!result.equals(ptr(-1))&&observed.size<256)observed.add(this.gif);}}));
const photoHover = typeof photoHoverFilter === 'function' ? photoHoverFilter() : null;
const photoHoverTimer = photoHover ? setInterval(() => photoHover.tick(), 60) : null;
function tick(){
  if(stopped)return;
  watchedWindows=watchedWindows.filter(w=>windowPid(w,pidOut)===uiThread&&pidOut.readU32()===Process.id);
  if(!watchedWindows.length&&!BRIDGE_CONFIG.managed){stopped=true;stats.stoppedReason='Chat window closed or replaced';return;}
  let delay=500;
  for(const s of sources.values())if(s.frames&&Date.now()-s.lastSeen>20000)release(s);
  const visible=watchedWindows.filter(w=>isVisible(w)&&!isIconic(w));
  if(visible.length&&sources.size){
    for(const w of visible)refreshConversation(w);
    for(const s of sources.values())if(s.frames){
      if(Date.now()-s.lastSeen>20000){release(s);continue;}
      if(Date.now()-s.lastSeen<1000){const elapsed=(Date.now()-s.started)%s.duration;delay=Math.min(delay,Math.max(MIN_REFRESH_MS,s.frames.find(f=>elapsed<f.end).end-elapsed));}
    }
  }
  timer=setTimeout(tick,delay);
}
timer=setTimeout(tick,100);
rpc.exports={
  setwindows(handles){watchedWindows=[...new Set(handles)].map(w=>ptr(w)).filter(w=>windowPid(w,pidOut)===uiThread&&pidOut.readU32()===Process.id);return watchedWindows.length;},
  setphotofilter(room,sender,hide=false){if(photoHover)photoHover.configure(room,sender,hide);},
  restorephotos(){if(photoHover)photoHover.restore();},
  registerfile:register,unregister,activate,
  registerpreview(path,id,meta){return register(path,id,meta);},
  updatepath(id,path){const s=sources.get(id);if(s&&!s.external)s.path=path;},
  work(){const files=[...observed];observed.clear();return {files,pending:[...pending]};},
  snapshot(){return {...stats,windows:watchedWindows.map(w=>w.toString()),referenceBytes:refBytes,decodedBytes,bitmapCacheBytes,photoFilter:photoHover?.status()};},
  stop(){if(cleaned)return;cleaned=true;stopped=true;clearTimeout(timer);if(photoHoverTimer)clearInterval(photoHoverTimer);for(const l of listeners)l.detach();for(const s of sources.values())release(s);sources.clear();buckets.clear();clearMatches();for(const w of watchedWindows)refreshConversation(w);}

};
send({status:'bridge-armed',mode:'automatic-multiple-gifs'});
