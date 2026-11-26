const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const code=fs.readFileSync(__dirname+'/kakao-qt-live.js','utf8');
const expression=code.split("counts.photoStates=photos.map(p=>expression(p.object,p.context,\'")[1].split("\'")[0];
function inspect(overrides={}){
 const env={visible:true,width:120,height:120,parent:null,mapToItem:()=>({x:10,y:10}),
  item:{Window:{window:{visible:true,width:500,height:500}}},upload:{enabled:false},preferredsize:120,
  details:{isGif:true,extension:'.gif',originalCachePath:'private-original'},...overrides};
 return JSON.parse(vm.runInNewContext(expression,env));
}
assert.equal(inspect().gif,true);
const album=inspect({preferredsize:undefined,modelData:{isWebp:true,extension:'',originalCachePath:'private-album'}});
assert.equal(album.album,true);assert.equal(album.webp,true);assert.equal(album.hasOriginal,true);
assert.equal(inspect({parent:{clip:true,width:5,height:5,parent:null}}).onScreen,false);
assert.equal(inspect({mapToItem:()=>({x:10,y:600})}).onScreen,false);
assert.equal(inspect({upload:{enabled:true}}).uploading,true);
assert.equal(inspect({details:{extension:'.jpg',originalCachePath:'private'}}).gif,false);
assert.ok(!JSON.stringify(album).includes('private'));
console.log('Qt metadata: GIF, extensionless album WebP, clip, upload and privacy checks passed');
const qml=fs.readFileSync(__dirname+'/kakao-qt-photo.qml','utf8');
function extract(text,name){const start=text.indexOf('function '+name+'(');assert.ok(start>=0);let end=text.indexOf('{',start),depth=1;while(depth&&++end<text.length){if(text[end]==='{')depth++;else if(text[end]==='}')depth--;}return text.slice(start,end+1);}
const scheduler={};vm.runInNewContext(extract(code,'downloadSlots'),scheduler);
let rows=Array.from({length:8},(_,i)=>({id:String(i),key:'key'+i,eligible:true,ready:false,retryAt:0,granted:i<4}));
assert.deepEqual([...scheduler.downloadSlots(rows,1000)],['0','1','2','3']);
rows[2].ready=true;assert.deepEqual([...scheduler.downloadSlots(rows,1000)],['0','1','3','4']);
rows[0].retryAt=2000;rows[1].eligible=false;rows[4].key=rows[3].key;
assert.deepEqual([...scheduler.downloadSlots(rows,1000)],['3','5','6','7']);
assert.ok(scheduler.downloadSlots(rows,3000).has('0'));
let now=1000,starts=0,stops=0;
const pump={Date:{now:()=>now},downloadGranted:true,autoDownload:true,eligible:true,onScreen:true,downloadFailed:false,requestStartedAt:0,retryAt:0,downloadAttempts:0,requestedOriginal:'',media:{originalAvailable:false},SourceProvider:{COMPLETED:3},SourceDownloader:{COMPLETED:2},originalProvider:{status:1},originalDownloader:{status:0,source:'private',start(){starts++;},stop(){stops++;}}};
vm.runInNewContext(extract(qml,'pumpDownload'),pump);
pump.pumpDownload();assert.equal(starts,1);pump.pumpDownload();assert.equal(starts,1);
now=61000;pump.pumpDownload();assert.equal(stops,1);assert.equal(starts,1);
now=66000;pump.pumpDownload();assert.equal(starts,2);
now=126000;pump.pumpDownload();now=136000;pump.pumpDownload();assert.equal(starts,3);
now=196000;pump.pumpDownload();now=999999;pump.pumpDownload();assert.equal(starts,3);assert.equal(pump.downloadFailed,true);
pump.downloadFailed=false;pump.downloadAttempts=0;pump.autoDownload=false;pump.pumpDownload();assert.equal(starts,3);
pump.autoDownload=true;pump.onScreen=false;pump.pumpDownload();assert.equal(starts,3);
console.log('Qt download queue: concurrency, out-of-order completion, dedup, backoff, timeout cap and disabled/offscreen passed');

const metricClock={};vm.runInNewContext(extract(code,'detailedStatsDue'),metricClock);
let lastDetailed=0,detailRuns=0,heartbeats=0;
for(let clock=500;clock<=10000;clock+=500){heartbeats++;if(metricClock.detailedStatsDue(clock,lastDetailed)){detailRuns++;lastDetailed=clock;}}
assert.equal(heartbeats,20);assert.equal(detailRuns,10);
assert.ok(code.includes('heartbeat(o);if(!collectDetails)continue;'));
assert.ok(qml.includes('playing: frameCount > 1 && eligible && onScreen && status === AnimationImage.Ready'));
console.log('Qt playback telemetry: heartbeat/download cadence retained, detailed reads halved, native animation timing unchanged');
