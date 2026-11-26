const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const code=fs.readFileSync(require('node:path').join(__dirname,'kakao-original-download.js'),'utf8');
const fn=code.slice(code.indexOf('function downloadPump(){'),code.indexOf('\nfor(const [rva,first]'));
let now=1000;
const ready=new Set(),requested=[],saved=[];
const context={downloadBusy:false,downloadStopping:false,downloadLast:0,seedCandidates:[],DOWNLOAD_CONCURRENCY:4,
 downloadJobs:new Map(),downloadStats:{requested:0,saved:0,maxInFlight:0,events:[]},
 Date:{now:()=>now},DOWNLOAD_CONFIG:{directory:'test',session:'test'},Memory:{allocUtf16String:x=>x},
 originalDownload:object=>{requested.push(object);return 0;},originalReady:j=>ready.has(j.id),
 originalSave:object=>{saved.push(object);return 1;},send:()=>{},downloadError:e=>{throw e;},
 destroyJob:key=>context.downloadJobs.delete(key)};
for(let id=1;id<=8;id++)context.downloadJobs.set(id,{id,object:id,started:now,requested:false,format:'gif'});
vm.createContext(context);vm.runInContext(fn,context);
context.downloadPump();assert.deepEqual(requested,[1,2,3,4]);assert.equal(context.downloadStats.maxInFlight,4);
now+=1000;ready.add(3);context.downloadPump();assert.deepEqual(saved,[3],'third completed job must not wait for first');
now+=1000;context.downloadPump();assert.deepEqual(requested,[1,2,3,4,5]);assert.equal(context.downloadStats.maxInFlight,4);
now+=61000;context.downloadPump();assert.ok(context.downloadJobs.has(6),'queued jobs must not expire before request');
context.downloadStopping=true;now+=1000;context.downloadPump();assert.equal(context.downloadJobs.size,0);
context.downloadStopping=false;requested.length=0;now+=1000;
for(let id=10;id<=14;id++)context.downloadJobs.set(id,{id,object:id,requested:false,priority:id===14?2:0});
context.downloadPump();assert.deepEqual(requested,[14,10,11,12],'visible thumbnail takes the first available slot');
context.downloadStopping=true;context.downloadPump();assert.equal(context.downloadJobs.size,0,'stopping must drain even inside the normal 250ms throttle interval');
const formatFn=code.slice(code.indexOf('function thumbnailFormat('),code.indexOf('function queueThumbnail('));
vm.runInContext(formatFn,context);
function thumb(gif,url){return {add:offset=>offset===0xc0?{readU8:()=>gif}:{readPointer:()=>({sub:()=>({readU32:()=>url.length}),readUtf8String:()=>url})}};}
assert.equal(context.thumbnailFormat(thumb(1,'https://example.invalid/image')), 'gif');
assert.equal(context.thumbnailFormat(thumb(0,'https://example.invalid/image.WEBP?token=local')), 'webp');
assert.equal(context.thumbnailFormat(thumb(0,'https://example.invalid/image.png?name=webp')), null);
assert.equal(context.thumbnailFormat(thumb(0,'https://example.invalid/webp')), null);
console.log('Download concurrency, out-of-order completion, queued timeout and cleanup checks passed');
