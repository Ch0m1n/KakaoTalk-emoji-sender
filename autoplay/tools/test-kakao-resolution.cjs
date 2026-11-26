const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync(__dirname+'/kakao-auto-bridge.js','utf8');
const context={SIGNATURE_SIZE:96,sources:new Map(),buckets:new Map(),stats:{matches:0,ambiguous:0,comparisons:[]}};
// Native implementations are checked independently in the x86 fixture.
context.nativeBytes=x=>x;
context.equalBytes=(a,b,n)=>a.slice(0,n).every((v,i)=>v===b[i])?1:0;
context.hashBytes=(a,n)=>{let h=2166136261;for(let i=0;i<n;i++)h=Math.imul(h^a[i],16777619)>>>0;return h;};
for(const name of ['same','fingerprint','compatibleAspect','mapSourceRect','coverRect','sourceCandidates','matchResult','mapMatchedRect','coarsePixels','chooseSource']){
  const start=source.indexOf('function '+name+'(');let end=source.indexOf('{',start),depth=1;
  while(depth&&++end<source.length){if(source[end]==='{')depth++;else if(source[end]==='}')depth--;}
  vm.runInNewContext(source.slice(start,end+1),context);
}
let cases=0;
for(const [sw,sh] of [[200,200],[210,300],[300,210],[100,300]])for(const scale of [.75,1,1.25,1.5,1.75,2,2.5,3,4]){
  const w=Math.round(sw*scale),h=Math.round(sh*scale);
  assert.ok(context.compatibleAspect(w,h,sw,sh));
  for(const [x,y,rw,rh] of [[0,0,w,h],[0,Math.floor(h/3),w,Math.floor(h/2)],[w-1,h-1,1,1]]){
    const rect=context.mapSourceRect(w,h,x,y,rw,rh,sw,sh);
    assert.ok(rect&&rect.x>=0&&rect.y>=0&&rect.w>0&&rect.h>0&&rect.x+rect.w<=sw&&rect.y+rect.h<=sh);cases++;
  }
}
assert.equal(context.mapSourceRect(300,300,0,0,301,300,200,200),null);
assert.equal(context.mapSourceRect(300,300,-1,0,100,100,200,200),null);
assert.equal(context.compatibleAspect(300,200,200,200),false);
const ref=Uint8Array.from({length:88*88*3},(_,i)=>Math.floor(i/3)%88<44?20:230);
function add(id,bytes){context.sources.set(id,{id,w:200,h:200,refs:[bytes],coarse:[context.coarsePixels(bytes,96,96)]});const key=context.fingerprint(bytes);context.buckets.set(key,[...(context.buckets.get(key)||[]),id]);}
add('one',ref);assert.equal(context.chooseSource(ref,300,300).id,'one');
assert.equal(context.chooseSource(Uint8Array.from(ref,x=>x+2),800,800).id,'one');
assert.equal(context.chooseSource(Uint8Array.from(ref,x=>255-x),300,300),null);
add('duplicate',ref);assert.equal(context.chooseSource(ref,300,300),null);
assert.equal(context.chooseSource(Uint8Array.from(ref,x=>x+2),300,300),null);
assert.ok(!source.includes('s.w===w&&s.h===h'));
assert.ok(source.includes('same(actual,old.pixels)'),'GDI handle reuse must validate content');
assert.ok(source.includes('cache.size>=MAX_MATCH_CACHE'));
// Same HBITMAP may be repainted/reused without DeleteObject. Validate content,
// including a previous negative result, before serving a cached match.
const matchCode=source.slice(source.indexOf('function bitmapView('),source.indexOf('\nconst blend='));
const cacheContext={Process:{pointerSize:8},cache:new Map(),sources:new Map([['one',{w:200,h:200}]]),MAX_MATCH_CACHE:128,bitmapCacheBytes:0,
  stats:{performance:{calls:0,matchMs:0,fromBitmapMs:0,normalizeMs:0,chooseMs:0}},
  sourceCandidates:()=>[{}],same:context.same,Memory:{alloc:()=>({readPointer:()=>1})},ptr:()=>0,
  fromBitmap:()=>0,dispose:()=>{},normalizedPixels:()=>[cacheContext.content],
  chooseSource:()=>cacheContext.content[0]===7?'one':null,content:new Uint8Array([0])};
vm.runInNewContext(matchCode,cacheContext);const handle={toString:()=> 'same-handle'};
assert.equal(cacheContext.match(handle,300,300),null);
cacheContext.content=new Uint8Array([7]);assert.equal(cacheContext.match(handle,300,300),'one');
cacheContext.content=new Uint8Array([9]);assert.equal(cacheContext.match(handle,300,300),null);
for(let i=0;i<300;i++)cacheContext.match({toString:()=>String(i)},300,300);
assert.equal(cacheContext.cache.size,128);
// The fast path validates all bytes, including negative matches, and cannot
// grow beyond its byte budget even if the handle-count limit is not reached.
cacheContext.clearMatches();cacheContext.MAX_BITMAP_CACHE=12;
cacheContext.Memory.alloc=n=>new Uint8Array(n);
cacheContext.Memory.copy=(out,input,n)=>out.set(input.slice(0,n));
cacheContext.equalBytes=(a,b,n)=>a.slice(0,n).every((v,i)=>v===b[i])?1:0;
cacheContext.stats.performance.bitmapCacheHits=0;
const raw=new Uint8Array([0,0,0,0]);
const realBitmapView=cacheContext.bitmapView;
cacheContext.flushGdi=()=>true;
function bitmapInfo({stride=4,planes=1,bpp=32,nullBits=false}={}){
  return {add:offset=>({readS32:()=>stride,readU16:()=>offset===16?planes:bpp,readPointer:()=>({isNull:()=>nullBits})})};
}
assert.equal(realBitmapView(null,1,1,bitmapInfo()).size,4);
for(const options of [{nullBits:true},{planes:2},{bpp:24},{stride:2},{stride:65540}])assert.equal(realBitmapView(null,1,1,bitmapInfo(options)),null);
assert.equal(realBitmapView(null,1,4,bitmapInfo()),null,'oversized storage falls back');
cacheContext.flushGdi=()=>false;assert.equal(realBitmapView(null,1,1,bitmapInfo()),null);
cacheContext.bitmapView=()=>({bits:raw,stride:4,size:4});
cacheContext.fromBitmap=()=>{throw Error('Unexpected GDI+ conversion on cache hit');};
cacheContext.rememberMatch('same-handle',{w:300,h:300,pixels:new Uint8Array([0]),id:null},cacheContext.bitmapView());
assert.equal(cacheContext.match(handle,300,300),null);
assert.equal(cacheContext.stats.performance.bitmapCacheHits,1);
raw[3]=1;assert.throws(()=>cacheContext.match(handle,300,300),/Unexpected GDI/,'last byte must invalidate a cached miss');
for(let i=0;i<20;i++)cacheContext.rememberMatch(String(i),{w:300,h:300,id:'one'},cacheContext.bitmapView());
assert.equal(cacheContext.bitmapCacheBytes,12);assert.equal(cacheContext.cache.size,3);
cacheContext.forgetMatch('19');assert.equal(cacheContext.bitmapCacheBytes,8);
cacheContext.clearMatches();assert.equal(cacheContext.bitmapCacheBytes,0);
// A crop must preserve the displayed subrectangle at every DPI.
for(const [w,h] of [[82,300],[300,82]]){
  const crop=context.coverRect(w,h);assert.ok(crop);
  for(const scale of [.75,1,1.5,2,3,4]){
    const tw=Math.round(crop.w*scale),th=Math.round(crop.h*scale);
    const result=context.mapMatchedRect(tw,th,0,0,tw,th,{w,h},{crop});
    assert.equal(JSON.stringify(result),JSON.stringify(crop));
    const part=context.mapMatchedRect(tw,th,0,Math.floor(th/2),tw,th-Math.floor(th/2),{w,h},{crop});
    assert.ok(part.x>=crop.x&&part.y>=crop.y&&part.x+part.w<=crop.x+crop.w&&part.y+part.h<=crop.y+crop.h);
  }
}
assert.equal(context.coverRect(100,100),null);
assert.equal(context.coverRect(30,300),null,'do not guess heavily cropped thumbnails');
// Invoke the actual shipped draw callback with fake native pointers. It must
// map the source crop but preserve every destination and blend argument.
const p=n=>({toInt32:()=>n,toString:()=>String(n),isNull:()=>false});
let callback;
const drawContext={BITMAP_SIZE:32,busy:false,stopped:false,uiThread:42,app:{base:{add:n=>n}},
  stats:{sizes:{},replacements:0,errors:[]},Memory:{alloc:()=>({add:offset=>({readS32:()=>offset===4?400:600})})},
  currentObject:()=>p(1),getObject:()=>true,mapSourceRect:context.mapSourceRect,mapMatchedRect:context.mapMatchedRect,match:()=> ({id:'one',crop:null}),ptr:p,
  sources:new Map([['one',{w:200,h:300,frames:[{dc:p(99),end:1000}],started:Date.now(),duration:1000,lastCycle:0,report:{loops:0,replacements:0,frameUse:{}}}]]),
  pending:new Set(),listeners:[],blend:1,Interceptor:{attach:(_,cb)=>{callback=cb;}}};
const hook=source.slice(source.indexOf('listeners.push(Interceptor.attach(blend,'),source.indexOf("listeners.push(Interceptor.attach(gdi."));
vm.runInNewContext(hook,drawContext);
for(const [dw,dh] of [[1280,720],[1920,1080],[3840,2160],[7680,4320]]){
  const args=[1,20,30,dw,dh,2,40,60,200,300,0x1ff0000].map(p),before=args.slice();
  callback.onEnter.call({threadId:42,returnAddress:{equals:()=>true,compare:address=>typeof address==='object'?1:-1}},args);
  for(const i of [0,1,2,3,4,10])assert.equal(args[i],before[i]);
  assert.deepEqual(args.slice(6,10).map(x=>x.toInt32()),[20,30,100,150]);
}
assert.equal(drawContext.stats.replacements,4);assert.equal(drawContext.stats.errors.length,0);
console.log(`Resolution regression: ${cases} scaled/cropped rectangles and matching/ambiguity checks passed`);
