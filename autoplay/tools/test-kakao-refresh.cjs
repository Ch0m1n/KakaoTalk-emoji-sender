const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync(__dirname+'/kakao-auto-bridge.js','utf8');
function block(name){const start=source.indexOf('function '+name+'('),end=source.indexOf('\nfunction ',start+1);return source.slice(start,end<0?undefined:end);}
function memory(){const data={values:{},text:''};function at(offset){return {add:n=>at(offset+n),readS32:()=>data.values[offset]||0,writeS32:n=>{data.values[offset]=n;},readUtf16String:()=>data.text,_data:data};}return at(0);}
let cases=0;
for(const scale of [.75,1,1.25,1.5,2,3,4])for(const editY of [250,400,500]){
 const width=Math.round(400*scale),height=Math.round(600*scale),top=Math.round(editY*scale),draws=[];
 const windows={1:{x:0,y:0,w:width,h:height,cls:'CHAT'},2:{x:0,y:0,w:width,h:height,cls:'HOST'},3:{x:0,y:top,w:width,h:height-top,cls:'RICHEDIT50W'},4:{x:0,y:0,w:width,h:top,cls:'VIEW'},5:{x:0,y:height-20,w:width,h:20,cls:'BUTTON'}};
 const context={WIN_ABI:'stdcall',Process:{arch:'ia32',pointerSize:4},stats:{refreshRequests:0,refreshSkipped:0},Memory:{alloc:memory},ptr:()=>0,NativeCallback:function(fn){return fn;},
  isVisible:()=>true,refreshClient:(window,rect)=>{rect.writeS32(0);rect.add(4).writeS32(0);rect.add(8).writeS32(windows[window].w);rect.add(12).writeS32(windows[window].h);return 1;},
  refreshMap:(from,to,rect)=>{const dx=windows[from].x-windows[to].x,dy=windows[from].y-windows[to].y;for(const offset of [0,8])rect.add(offset).writeS32(rect.add(offset).readS32()+dx);for(const offset of [4,12])rect.add(offset).writeS32(rect.add(offset).readS32()+dy);},
  refreshClass:(window,name)=>{name._data.text=windows[window].cls;},refreshChildren:(_,fn)=>{for(const id of [2,3,4,5])if(!fn(id,0))break;},
  redraw:(window,rect,_,flags)=>draws.push({window,flags,top:rect.add(4).readS32()+windows[window].y,bottom:rect.add(12).readS32()+windows[window].y})};
 for(const name of ['clipRefreshRect','conversationCutoff','clientInRoot','refreshConversation']){
   let code=block(name);if(name==='refreshConversation')code=source.slice(source.indexOf('function refreshConversation('),source.indexOf('\nstats.performancePatch='));vm.runInNewContext(code,context);
 }
 context.refreshConversation(1);assert.equal(draws.length,3);
 assert.ok(draws.every(d=>d.window!==3&&d.window!==5&&d.bottom<top&&d.flags===0x61&&!(d.flags&0x80)));
 windows[3].cls='UNKNOWN';draws.length=0;context.refreshConversation(1);assert.equal(draws.length,0,'no editor proof must never fall back to full-window redraw');
 cases++;
}
assert.ok(!source.includes('redraw(w,ptr(0),ptr(0),0x81)'));
console.log(`Conversation repaint: ${cases} DPI/layout cases passed; editor/send strip excluded, no background erase or child propagation`);
