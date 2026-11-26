// Version-locked experimental original acquisition. No URL/credential logging.
const downloadBase=Process.getModuleByName('KakaoTalk.exe').base;
if(Process.arch!=='ia32'||!downloadBase.equals(ptr('0x400000')))throw Error('Unsupported image');
// Verify inspected function bodies and object tables before internal callers.
// Entry opcodes below also reject existing hooks; no version/file-hash gate.
const inspectedDownloadEntries=[[15440704,"5083ec085657a180e1930233c5508d45f464a3000000008bf9897decc745f000"],[15441552,"5083ec58a180e1930233c58945f0535657508d45f464a3000000008bd18955b0"],[15432512,"896c24048bec6aff6842f7230264a100000000505383ec44a180e1930233c589"],[15434352,"5081ec88000000a180e1930233c58945f05657508d45f464a3000000008bf989"],[15435840,"5081ec70020000a180e1930233c58945f05657508d45f464a3000000008bf933"],[15438512,"5081eca4000000a180e1930233c58945f05657508d45f464a3000000008bf189"],[15433504,"0e68d000000056e87f71e30083c4088bc65e5dc20400cccccccccccccccccccc"],[35415540,"107f2b0160822b0130882b0110812b01"],[35415464,"b09c2b01809e2b01e0a22b01b09d2b01"],[35309812,"f0f8870010e3870060441d01b0f48700"]];
for(const [rva,expected] of inspectedDownloadEntries){
 let actual='';try{actual=[...new Uint8Array(downloadBase.add(rva).readByteArray(expected.length/2))].map(b=>b.toString(16).padStart(2,'0')).join('');}catch(_){}
 if(actual!==expected)throw Error('Unexpected download entry layout rva=0x'+rva.toString(16));
}

const downloadHooks=[],downloadJobs=new Map(),downloadSeen=new Set();
const DOWNLOAD_CONCURRENCY=4;
const downloadStats={created:0,backfilled:0,backfilledFormats:{gif:0,webp:0},seedCandidates:0,seedBytes:0,requested:0,saved:0,maxInFlight:0,errors:[],events:[]};
const seedCandidates=[];
let downloadBusy=false,downloadStopping=false,downloadLast=0,downloadSerial=0;
const originalCtor=new NativeFunction(downloadBase.add(0xeb7b30),'pointer',['pointer','pointer','int'],'thiscall');
const originalDestroy=new NativeFunction(downloadBase.add(0xeb7f10),'pointer',['pointer','uint'],'thiscall');
const originalGet=new NativeFunction(downloadBase.add(0xeb8260),'pointer',['pointer','pointer'],'thiscall');
const originalDownload=new NativeFunction(downloadBase.add(0xeb8830),'int',['pointer'],'thiscall');
const originalSave=new NativeFunction(downloadBase.add(0xeb92a0),'int',['pointer','pointer'],'thiscall');
function downloadError(e){if(downloadStats.errors.length<20)downloadStats.errors.push(String(e));}
function destroyJob(key,job){originalDestroy(job.object,0);downloadJobs.delete(key);}
function originalReady(job){
  const value=Memory.alloc(8);value.writeByteArray([0,0,0,0,255,0,0,0]);
  originalGet(job.object,value);
  const img=value.readPointer();
  if(img.isNull())return false;
  // GetImage's returned SmartPtr owns one temporary reference (verified caller cleanup).
  const release=new NativeFunction(img.readPointer().add(8).readPointer(),'void',['pointer'],'stdcall');
  release(img);return true;
}
function downloadPump(){
  if(downloadBusy||(!downloadStopping&&Date.now()-downloadLast<250))return;
  downloadLast=Date.now();downloadBusy=true;
  try{
    for(let added=0;!downloadStopping&&seedCandidates.length&&downloadJobs.size<16&&added<DOWNLOAD_CONCURRENCY;added++){
      const thumb=seedCandidates.shift();
      try{queueThumbnail(thumb);}catch(_){} // Candidate may have been freed since the bounded scan.
    }
    for(const [key,job] of downloadJobs){
      if(downloadStopping||(job.requested&&Date.now()-job.requestedAt>60000)){destroyJob(key,job);continue;}
    }
    let inFlight=[...downloadJobs.values()].filter(job=>job.requested).length;
    for(const job of [...downloadJobs.values()].sort((a,b)=>(b.priority||0)-(a.priority||0))){
      if(downloadStopping||inFlight>=DOWNLOAD_CONCURRENCY)break;
      if(!job.requested){
        job.requested=true;job.requestedAt=Date.now();job.result=originalDownload(job.object);downloadStats.requested++;inFlight++;
        downloadStats.events.push({id:job.id,event:'requested',result:job.result,format:job.format});
        if(downloadStats.events.length>256)downloadStats.events.shift();
      }
    }
    downloadStats.maxInFlight=Math.max(downloadStats.maxInFlight,inFlight);
    for(const [key,job] of downloadJobs){
      if(!job.requested)continue;
      if(originalReady(job)){
        const path=DOWNLOAD_CONFIG.directory+'\\original-'+DOWNLOAD_CONFIG.session+'-'+job.id+'.'+job.format;
        const result=originalSave(job.object,Memory.allocUtf16String(path));
        downloadStats.events.push({id:job.id,event:'save',result});
        if(downloadStats.events.length>256)downloadStats.events.shift();
        if(result===1){send({originalFile:path,result});downloadStats.saved++;destroyJob(key,job);}
        else if(result!==0){downloadError('SaveImage returned '+result);destroyJob(key,job);}
      }
    }
  }catch(e){downloadError(e);downloadStopping=true;}finally{downloadBusy=false;}
}
for(const [rva,first] of [[0xeb9b30,0x55],[0xeb9e80,0x55],[0xeb7b30,0x53],[0xeb8260,0x55],[0xeb8830,0x55],[0xeb92a0,0x55],[0xeb7f10,0x55]]){
  const actual=downloadBase.add(rva).readU8();
  if(actual!==first)throw Error('Unexpected download entry rva=0x'+rva.toString(16)+' expected=0x'+first.toString(16)+' actual=0x'+actual.toString(16));
}
function queueOriginal(reference,priority=0){
    try{
      const mime=reference.readPointer().add(0x44).readPointer();
      const length=mime.sub(8).readU32();
      if(length<1||length>32)return;
      const type=mime.readUtf8String(length).toLowerCase();
      if(type!=='image/gif'&&type!=='image/webp')return;
      // Keep deduplication entirely in process; never log message IDs or URLs.
      const descriptor=reference.readPointer(),url=descriptor.add(0x30).readPointer();
      const urlLength=url.sub(8).readU32();
      if(urlLength>4096)return;
      const identity=descriptor.readU64().toString()+':'+url.readUtf8String(urlLength);
      if(downloadSeen.has(identity)){
        for(const job of downloadJobs.values())if(job.identity===identity)job.priority=Math.max(job.priority,priority);
        return;
      }
      const object=Memory.alloc(0xd0);
      const wasBusy=downloadBusy;
      downloadBusy=true;
      try{originalCtor(object,reference,0);}finally{downloadBusy=wasBusy;}
      if(!object.readPointer().equals(downloadBase.add(0x21c65f4)))throw Error('Unexpected original vtable');
      const id=++downloadSerial;
      downloadJobs.set(id,{id,object,identity,priority,format:type.slice(6),started:Date.now(),requested:false});
      downloadSeen.add(identity);
      if(downloadSeen.size>4096)downloadSeen.delete(downloadSeen.values().next().value);
      downloadStats.created++;
      return true;
    }catch(e){downloadError(e);downloadStopping=true;}
}
function canQueue(thread){return !downloadStopping&&!downloadBusy&&thread===DOWNLOAD_CONFIG.thread&&downloadJobs.size<16;}
downloadHooks.push(Interceptor.attach(downloadBase.add(0xeb9b30),{
  onEnter(args){if(canQueue(this.threadId))queueOriginal(args[0],1);}
}));
// Existing GIF/WebP thumbnail containers keep original URL metadata.
// Reconstitute only the verified fields read by the original constructor. Its own
// constructors copy/refcount their strings and optional download metadata before return.
// Never substitute a thumbnail object as an original object (layouts differ).
const backfillMimes={};
for(const format of ['gif','webp']){
  const storage=Memory.alloc(32),value=storage.add(16),type='image/'+format;
  value.writeUtf8String(type);value.sub(8).writeU32(type.length);
  backfillMimes[format]={storage,value};
}
function thumbnailFormat(thumb){
  if(thumb.add(0xc0).readU8()===1)return 'gif';
  const url=thumb.add(0x50).readPointer(),length=url.sub(8).readU32();
  if(length<1||length>4096)return null;
  // This field was verified against newly received WebP descriptors. Never log it.
  return /\.webp(?:$|[?#])/i.test(url.readUtf8String(length))?'webp':null;
}
function queueThumbnail(thumb,priority=0){
      if(!thumb.readPointer().equals(downloadBase.add(0x21c65a8)))return;
      const control=thumb.sub(0x10);
      if(!control.readPointer().equals(downloadBase.add(0x21ac8f4))||
         !thumb.add(4).readPointer().equals(thumb)||!thumb.add(8).readPointer().equals(control)||
         control.add(4).readS32()<1||control.add(4).readS32()>1000||control.add(8).readS32()<1)return;
      const format=thumbnailFormat(thumb);if(!format)return;
      const descriptor=Memory.alloc(0xe0),reference=Memory.alloc(4);
      descriptor.writeByteArray(new Uint8Array(0xe0));reference.writePointer(descriptor);
      Memory.copy(descriptor,thumb.add(0x38),8);
      Memory.copy(descriptor.add(8),thumb.add(0x30),8);
      Memory.copy(descriptor.add(0x10),thumb.add(0x40),8);
      Memory.copy(descriptor.add(0x20),thumb.add(0x48),4);
      descriptor.add(0x30).writePointer(thumb.add(0x50).readPointer());
      descriptor.add(0x3c).writePointer(downloadBase.add(0x2291fde));
      descriptor.add(0x40).writePointer(thumb.add(0x54).readPointer());
      descriptor.add(0x44).writePointer(backfillMimes[format].value);
      Memory.copy(descriptor.add(0x70),thumb.add(0x58),16);
      Memory.copy(descriptor.add(0x80),thumb.add(0x68),0x55);
      if(queueOriginal(reference,priority)){downloadStats.backfilled++;downloadStats.backfilledFormats[format]++;}
}
downloadHooks.push(Interceptor.attach(downloadBase.add(0xeb9e80),{
  onEnter(){
    if(!canQueue(this.threadId))return;
    try{queueThumbnail(this.context.ecx,2);}catch(e){downloadError(e);downloadStopping=true;}
  }
}));
for(const name of ['DispatchMessageW','DispatchMessageA']){
  downloadHooks.push(Interceptor.attach(Process.getModuleByName('user32.dll').getExportByName(name),{
    onEnter(){if(this.threadId===DOWNLOAD_CONFIG.thread)downloadPump();}
  }));
}
const downloadUser=Process.getModuleByName('user32.dll');
const wakeDownloadWindow=new NativeFunction(downloadUser.getExportByName('PostMessageW'),'int',['pointer','uint','pointer','pointer'],'stdcall');
const downloadThreadWindows=new NativeFunction(downloadUser.getExportByName('EnumThreadWindows'),'int',['uint','pointer','pointer'],'stdcall');
function wakeDownloadCleanup(){
  const visit=new NativeCallback(window=>{wakeDownloadWindow(window,0,ptr(0),ptr(0));return 1;},'int',['pointer','pointer'],'stdcall');
  downloadThreadWindows(DOWNLOAD_CONFIG.thread,visit,ptr(0));
}
rpc.exports={
  seedexisting(){
    // Search only for this version's exact thumbnail vtable in bounded private
    // writable ranges. No raw memory, message text, URLs, or credentials leave
    // the process. Revalidate on the UI thread before copying image metadata.
    if(downloadStats.seedBytes)return downloadStats.seedCandidates;
    const modules=Process.enumerateModules();
    for(const range of Process.enumerateRanges({protection:'rw-',coalesce:false})){
      if(range.file||range.base.compare(ptr('0x1000000'))<0||range.base.compare(ptr('0x70000000'))>=0)continue;
      if(modules.some(m=>range.base.compare(m.base.add(m.size))<0&&range.base.add(range.size).compare(m.base)>0))continue;
      if(downloadStats.seedBytes+range.size>512*1024*1024)break;
      downloadStats.seedBytes+=range.size;
      try{
        for(const hit of Memory.scanSync(range.base,range.size,'a8 65 5c 02')){
          if(hit.address.toUInt32()%4||hit.address.add(0xc8).compare(range.base.add(range.size))>0)continue;
          try{
            const t=hit.address,c=t.sub(0x10);
            if(!c.readPointer().equals(downloadBase.add(0x21ac8f4))||
               !t.add(4).readPointer().equals(t)||!t.add(8).readPointer().equals(c)||
               c.add(4).readS32()<1||!thumbnailFormat(t))continue;
            seedCandidates.push(t);downloadStats.seedCandidates++;
          }catch(_){continue;}
          if(seedCandidates.length>=128)return seedCandidates.length;
        }
      }catch(_){}
    }
    return seedCandidates.length;
  },
  snapshot(){return {...downloadStats,concurrency:DOWNLOAD_CONCURRENCY,inFlight:[...downloadJobs.values()].filter(j=>j.requested).length,pending:downloadJobs.size,stopping:downloadStopping};},
  stop(){downloadStopping=true;if(downloadJobs.size)wakeDownloadCleanup();return downloadJobs.size;},
  detach(){if(downloadJobs.size)throw Error('Pending native objects must be cleaned on UI thread');for(const h of downloadHooks)h.detach();}
};
send({status:'original-download-armed'});
