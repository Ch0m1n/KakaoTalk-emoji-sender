'use strict';
if(Process.arch!=='x64'||Process.mainModule.name.toLowerCase()!=='kakaotalkui.exe')throw Error('Qt x64 target required');
const core=Process.getModuleByName('Qt6Core.dll'),gui=Process.getModuleByName('Qt6Gui.dll'),qml=Process.getModuleByName('Qt6Qml.dll'),quick=Process.getModuleByName('Qt6Quick.dll');
const nf=(m,n,r,a)=>new NativeFunction(m.getExportByName(n),r,a,'win64');
const app=nf(core,'?instance@QCoreApplication@@SAPEAV1@XZ','pointer',[]);
const children=nf(core,'?children@QObject@@QEBAAEBV?$QList@PEAVQObject@@@@XZ','pointer',['pointer']);
const inherits=nf(core,'?inherits@QObject@@QEBA_NPEBD@Z','bool',['pointer','pointer']);
const windows=nf(gui,'?allWindows@QGuiApplication@@SA?AV?$QList@PEAVQWindow@@@@XZ','pointer',['pointer']);
// Drop this probe's QList reference atomically (Win64 RCX points to refcount).
const refCode=Memory.alloc(Process.pageSize);refCode.writeByteArray([0xb8,0xff,0xff,0xff,0xff,0xf0,0x0f,0xc1,0x01,0xff,0xc8,0xc3]);Memory.protect(refCode,Process.pageSize,'r-x');
const unref=new NativeFunction(refCode,'int',['pointer'],'win64');
const free=nf(core,'?deallocate@QArrayData@@SAXPEAU1@_J1@Z','void',['pointer','int64','int64']);
const context=nf(qml,'?contextForObject@QQmlEngine@@SAPEAVQQmlContext@@PEBVQObject@@@Z','pointer',['pointer']);
const exprCtor=nf(qml,'??0QQmlExpression@@QEAA@PEAVQQmlContext@@PEAVQObject@@AEBVQString@@1@Z','pointer',['pointer','pointer','pointer','pointer','pointer']);
const exprDtor=nf(qml,'??1QQmlExpression@@UEAA@XZ','void',['pointer']);
const evaluate=nf(qml,'?evaluate@QQmlExpression@@QEAA?AVQVariant@@PEA_N@Z','pointer',['pointer','pointer','pointer']);
const hasError=nf(qml,'?hasError@QQmlExpression@@QEBA_NXZ','bool',['pointer']);
const strCtor=nf(core,'??0QString@@QEAA@PEBD@Z','pointer',['pointer','pointer']);
const varStr=nf(core,'?toString@QVariant@@QEBA?AVQString@@XZ','pointer',['pointer','pointer']);
const varDtor=nf(core,'??1QVariant@@QEAA@XZ','void',['pointer']);
const nameFor=nf(qml,'?nameForObject@QQmlContext@@QEBA?AVQString@@PEBVQObject@@@Z','pointer',['pointer','pointer','pointer']);
const parentContext=nf(qml,'?parentContext@QQmlContext@@QEBAPEAV1@XZ','pointer',['pointer']);
const sd=nf(core,'??1QString@@QEAA@XZ','void',['pointer']);
const visualChildren=nf(quick,'?childItems@QQuickItem@@QEBA?AV?$QList@PEAVQQuickItem@@@@XZ','pointer',['pointer','pointer']);
const itemClass=Memory.allocUtf8String('QQuickItem');
const content=nf(quick,'?contentItem@QQuickWindow@@QEBAPEAVQQuickItem@@XZ','pointer',['pointer']);
const visible=nf(quick,'?isVisible@QQuickItem@@QEBA_NXZ','bool',['pointer']);
const windowVisible=nf(gui,'?isVisible@QWindow@@QEBA_NXZ','bool',['pointer']);
const windowClass=Memory.allocUtf8String('QQuickWindow');
const imageClass=Memory.allocUtf8String('QQuickImage'),animationClass=Memory.allocUtf8String('AnimationImage');
const user=Process.getModuleByName('user32.dll');
const enumWindows=nf(user,'EnumWindows','int',['pointer','pointer']);
const windowPid=nf(user,'GetWindowThreadProcessId','uint',['pointer','pointer']);
const post=nf(user,'PostMessageW','int',['pointer','uint','pointer','pointer']);
let hwnd=ptr(0),uiThread=0,result=null,busy=false,lastScan=0,lastBeat=0,stopping=false;
const completedMedia=new Set();
const tracked=new Map(),stats={engine:'qt6-live',active:0,created:0,frameChanges:0,loops:0,originalsReady:0,errors:[],states:[],stopped:false};
const getProperty=nf(core,'?property@QObject@@QEBA?AVQVariant@@PEBD@Z','pointer',['pointer','pointer','pointer']);
const setProperty=nf(core,'?setProperty@QObject@@QEAA_NPEBDAEBVQVariant@@@Z','bool',['pointer','pointer','pointer']);
const numberVariant=nf(core,'??0QVariant@@QEAA@N@Z','pointer',['pointer','double']);
const deleteLater=nf(core,'?deleteLater@QObject@@QEAAXXZ','void',['pointer']);
const propertyNames=new Map();
function propName(n){if(!propertyNames.has(n))propertyNames.set(n,Memory.allocUtf8String(n));return propertyNames.get(n);}
function prop(o,n){const v=Memory.alloc(64),s=Memory.alloc(32);getProperty(o,v,propName(n));try{varStr(v,s);try{return s.add(8).readPointer().readUtf16String(s.add(16).readS64().toNumber());}finally{sd(s);}}finally{varDtor(v);}}
function numberProperty(o,n,value){const v=Memory.alloc(64);numberVariant(v,value);try{setProperty(o,propName(n),v);}finally{varDtor(v);}}
function heartbeat(o){numberProperty(o,'heartbeat',Date.now());}
function downloadSlots(rows,now,limit=4){
 const selected=new Set(),keys=new Set();
 for(const row of [...rows].sort((a,b)=>Number(b.granted)-Number(a.granted))){
  if(selected.size>=limit)break;
  if(!row.eligible||row.ready||row.retryAt>now||!row.key||keys.has(row.key))continue;
  selected.add(row.id);keys.add(row.key);
 }
 return selected;
}

function error(e){if(stats.errors.length<8)stats.errors.push(String(e).slice(0,180));}
// Native destructor observer never enters JS from a Qt render/worker thread.
const liveSlots=Memory.alloc(8*8);
const lifetimeModule=new CModule(`
#include <gum/guminterceptor.h>
extern gpointer live_slots[8];
void object_destroyed(GumInvocationContext *ctx) {
  gpointer object=gum_invocation_context_get_nth_argument(ctx,0);
  for(int i=0;i<8;i++) if(live_slots[i]==object) live_slots[i]=NULL;
}
`,{live_slots:liveSlots});
const destructing=Interceptor.attach(core.getExportByName('??1QObject@@UEAA@XZ'),lifetimeModule.object_destroyed);
const ownedSlots=new Map();
function alive(o){const slot=ownedSlots.get(o.toString());return slot!==undefined&&liveSlots.add(slot*8).readPointer().equals(o);}
function forget(o){const key=o.toString(),slot=ownedSlots.get(key);if(slot!==undefined)liveSlots.add(slot*8).writePointer(ptr(0));ownedSlots.delete(key);tracked.delete(key);}
function remember(o){for(let i=0;i<8;i++)if(liveSlots.add(i*8).readPointer().isNull()){liveSlots.add(i*8).writePointer(o);ownedSlots.set(o.toString(),i);tracked.set(o.toString(),o);return;}throw Error('Qt replay slots full');}

function expression(obj,ctx,code){
 const str=Memory.alloc(32),expr=Memory.alloc(4096),out=Memory.alloc(64),text=Memory.alloc(32);
 strCtor(str,Memory.allocUtf8String(code));exprCtor(expr,ctx,obj,str,ptr(0));sd(str);
 try{evaluate(expr,out,ptr(0));try{if(hasError(expr))return 'expression-error';varStr(out,text);try{return text.add(8).readPointer().readUtf16String(text.add(16).readS64().toNumber());}finally{sd(text);}}finally{varDtor(out);}}
 finally{exprDtor(expr);}
}
const find=new NativeCallback(w=>{const p=Memory.alloc(4),t=windowPid(w,p);if(p.readU32()===Process.id){hwnd=w;uiThread=t;return 0;}return 1;},'int',['pointer','pointer'],'win64');enumWindows(find,ptr(0));
function list(p){const n=p.add(16).readS64().toNumber(),data=p.add(8).readPointer();if(n<0||n>20000)throw Error('Invalid child count');return Array.from({length:n},(_,i)=>data.add(i*8).readPointer());}
function visit(){
 const roots=Memory.alloc(32);windows(roots);let queue=list(roots).filter(w=>inherits(w,windowClass)&&windowVisible(w)).map(w=>content(w));
 const d=roots.readPointer();if(!d.isNull()){const refs=d.readS32();if(refs>0&&unref(d)===0)free(d,8,8);}
 const seen=new Set(),photos=[],counts={objects:0,images:0,animations:0,thumbnails:0,names:{}};
 while(queue.length&&counts.objects<8000){
  const o=queue.pop(),id=o.toString();if(o.isNull()||seen.has(id))continue;seen.add(id);counts.objects++;
  if(!visible(o))continue;
  if(inherits(o,itemClass)){const v=Memory.alloc(32);visualChildren(o,v);queue.push(...list(v));const d=v.readPointer();if(!d.isNull()){const refs=d.readS32();if(refs>0&&unref(d)===0)free(d,8,8);}}
  const animated=inherits(o,animationClass),image=inherits(o,imageClass);if(animated)counts.animations++;if(!image&&!animated)continue;counts.images++;
  const ctx=context(o);if(ctx.isNull())continue;const name=Memory.alloc(32);nameFor(ctx,name,o);
  let text;try{text=name.add(8).readPointer().readUtf16String(name.add(16).readS64().toNumber());}finally{sd(name);}
  if(!text){let parent=parentContext(ctx);for(let depth=0;!parent.isNull()&&depth<8;depth++){const v=Memory.alloc(32);nameFor(parent,v,o);try{text=v.add(8).readPointer().readUtf16String(v.add(16).readS64().toNumber());}finally{sd(v);}if(text)break;parent=parentContext(parent);}}
  if(text&&/^[a-zA-Z_][a-zA-Z0-9_]*$/.test(text))counts.names[text]=(counts.names[text]||0)+1;
  if(text==='thumbnail'){counts.thumbnails++;photos.push({object:o,context:ctx});}
 }
 counts.photoStates=photos.map(p=>expression(p.object,p.context,'JSON.stringify((function(){var album=typeof modelData!=="undefined"&&Boolean(modelData?.originalCachePath);var m=album?modelData:(typeof details!=="undefined"?details:null);var p=mapToItem(null,0,0);var w=typeof item!=="undefined"?item.Window.window:null;return {photo:album||typeof preferredsize!=="undefined",album:album,webp:Boolean(m?.isWebp),gif:Boolean(m?.isGif)||Boolean(m?.animated),extension:m?.extension??"",hasOriginal:Boolean(m?.originalCachePath),uploading:typeof upload!=="undefined"&&upload.enabled,onScreen:(function(){var shown=visible&&w&&w.visible&&p.y+height>0&&p.y<w.height&&p.x+width>0&&p.x<w.width;for(var a=parent,n=0;shown&&a&&n<32;a=a.parent,n++){if(a.clip){var q=mapToItem(a,0,0);shown=q.x+width>0&&q.y+height>0&&q.x<a.width&&q.y<a.height;}}return Boolean(shown);})()};})())'));
 counts.created=[];
 if(typeof OVERLAY_SOURCE!=="undefined")for(let i=0;i<photos.length;i++){
  let state;try{state=JSON.parse(counts.photoStates[i]);}catch(_){continue;}
  if(!state.photo||!state.onScreen||state.uploading||!state.hasOriginal||!(state.webp||state.gif||/\.?(gif|webp)$/i.test(state.extension||"")))continue;
  const owner=photos[i].object;
  if(list(children(owner)).some(child=>tracked.has(child.toString())||(inherits(child,animationClass)&&prop(child,'objectName')==='KakaoAutoPlayQtPreview')))continue;
  if(tracked.size>=8)break;
  const created=expression(owner,photos[i].context,'Boolean(Qt.createQmlObject('+JSON.stringify(OVERLAY_SOURCE)+', thumbnail, "KakaoAutoPlayQtPreview"))');
  counts.created.push(created);
  if(created!=='true'){error('Qt photo component creation failed');continue;}
  for(const child of list(children(owner)))if(inherits(child,animationClass)&&prop(child,'objectName')==='KakaoAutoPlayQtPreview'&&!tracked.has(child.toString())){remember(child);stats.created++;}
 }
 return {counts,photos};
}
const dispatch=Interceptor.attach(user.getExportByName('DispatchMessageW'),{onEnter(){
 if(busy||Process.getCurrentThreadId()!==uiThread)return;busy=true;
 try{
  if(stopping){for(const o of tracked.values()){if(alive(o))deleteLater(o);forget(o);}stats.active=0;stats.states=[];stats.stopped=true;return;}
  const now=Date.now();
  if(now-lastBeat>=500){
   lastBeat=now;const beatRows=[];let frameChanges=0,loops=0;
   for(const o of tracked.values()){
    if(!alive(o)){stats.destroyed=(stats.destroyed||0)+1;forget(o);continue;}
    if(prop(o,'onScreen')!=='true'){stats.lastHidden={album:prop(o,'album'),eligible:prop(o,'eligible'),viewport:prop(o,'viewportVisible')};deleteLater(o);forget(o);continue;}
    heartbeat(o);const row={album:prop(o,'album')==='true',format:prop(o,'mediaFormat'),status:prop(o,'status'),originalStatus:Number(prop(o,'originalStatus')),downloadStatus:Number(prop(o,'downloadStatus')),frames:Number(prop(o,'frameCount')),changes:Number(prop(o,'observedFrames')),loops:Number(prop(o,'observedLoops')),playing:prop(o,'playing')==='true',attempts:Number(prop(o,'downloadAttempts')),downloadFailed:prop(o,'downloadFailed')==='true'};
    if(row.originalStatus===3){const key=prop(o,'mediaKey');if(key&&!completedMedia.has(key)){if(completedMedia.size>=4096)completedMedia.delete(completedMedia.values().next().value);completedMedia.add(key);stats.originalsReady++;}}
    beatRows.push(row);frameChanges+=row.changes;loops+=row.loops;
   }
   const requests=[...tracked.values()].map(o=>({object:o,id:o.toString(),key:prop(o,'mediaKey'),eligible:prop(o,'downloadEligible')==='true',ready:Number(prop(o,'originalStatus'))===3,retryAt:Number(prop(o,'retryAt')),granted:prop(o,'downloadGranted')==='true'}));
   const slots=downloadSlots(requests,now);
   // Revoke before granting so switching slots never exceeds the limit.
   for(const r of requests)if(r.granted&&!slots.has(r.id))numberProperty(r.object,'downloadGranted',0);
   for(const r of requests)if(!r.granted&&slots.has(r.id))numberProperty(r.object,'downloadGranted',1);
   stats.downloadPending=requests.filter(r=>r.eligible&&!r.ready).length;stats.downloadInFlight=slots.size;
   Object.assign(stats,{states:beatRows,frameChanges,loops,active:beatRows.filter(r=>r.playing&&r.frames>1).length});
  }
  if(now-lastScan>=3000){lastScan=now;const start=Date.now(),r=visit();stats.scanMs=Date.now()-start;stats.scanned=r.counts.objects;stats.photoCandidates=r.counts.thumbnails;stats.candidates=r.counts.photoStates.map(s=>{try{return JSON.parse(s);}catch(_){return {invalid:true};}});}
 }catch(e){error(e);}finally{busy=false;}
}});
const wake=setInterval(()=>{if(!hwnd.isNull()&&!stats.stopped)post(hwnd,0,ptr(0),ptr(0));},500);
rpc.exports={
 setwindows(handles){for(const handle of handles){const p=Memory.alloc(4),w=ptr(handle),t=windowPid(w,p);if(p.readU32()===Process.id){hwnd=w;uiThread=t;post(hwnd,0,ptr(0),ptr(0));return true;}}return false;},
 snapshot(){return stats;},
 stop(){stopping=true;if(!hwnd.isNull())post(hwnd,0,ptr(0),ptr(0));else stats.stopped=true;},
 detach(){if(!stats.stopped)throw Error('Wait for UI cleanup');clearInterval(wake);dispatch.detach();destructing.detach();}
};
