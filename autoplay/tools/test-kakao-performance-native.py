"""Compare shipped native pixel helpers with the original algorithm in our x86 fixture."""
import json, subprocess, sys, time
from pathlib import Path
ARCH='x64' if '--x64' in sys.argv else 'x86'
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'artifacts/kakao-internals/deps'))
import frida
work=ROOT/'artifacts/tests/performance-native';work.mkdir(parents=True,exist_ok=True)
identity=work/('fixture-'+str(time.time_ns())+'.json')
process=subprocess.Popen([str(ROOT/('artifacts/tests/autoplay-agent-load'+('-x64' if ARCH=='x64' else '')+'/KakaoTalk.exe')),str(identity)],creationflags=0x08000000)
session=None
try:
    for _ in range(100):
        if identity.exists():break
        time.sleep(.05)
    info=json.loads(identity.read_text());assert info['pid']==process.pid
    session=frida.attach(process.pid)
    source=(ROOT/'tools/kakao-auto-bridge.js').read_text(encoding='utf-8')
    helpers=source.split('// PERF_NATIVE_BEGIN:')[1].split('\n',1)[1].split('// PERF_NATIVE_END')[0]
    code=helpers+'''
rpc.exports.test=function(){
 let cases=0;
 for(const w of [9,16,96,210])for(const h of [9,17,96,300])for(const pad of [0,12])for(const sign of [1,-1]){
  const row=w*4+pad,size=row*h,storage=Memory.alloc(size),data=new Uint8Array(size);
  for(let i=0;i<size;i++)data[i]=(i*37+Math.floor(i/13))&255;
  storage.writeByteArray(data);const scan=sign===1?storage:storage.add((h-1)*row),stride=sign*row;
  const expected=[];for(let y=4;y<h-4;y++)for(let x=4;x<w-4;x++)for(let c=0;c<3;c++)expected.push(data[(sign===1?y:h-1-y)*row+x*4+c]);
  const actual=cropPixels(scan,stride,w,h),reference=new Uint8Array(expected);
  if(actual.length!==reference.length||actual.some((v,i)=>v!==reference[i]))throw Error('crop mismatch '+[w,h,pad,sign]);
  let hash=2166136261;for(const v of reference)hash=Math.imul(hash^v,16777619)>>>0;
  if(fingerprint(actual)!==hash.toString(16)||!same(actual,reference))throw Error('hash/equality mismatch');
  const changed=reference.slice();changed[changed.length-1]^=1;if(same(actual,changed))throw Error('last byte mismatch');
  cases++;
 }
 const backing=new Uint8Array([9,1,2,3,9]),sub=backing.subarray(1,4);
 if(!same(sub,new Uint8Array([1,2,3]))||same(sub,new Uint8Array([1,2])))throw Error('subarray/length');
 if(!same(new Uint8Array(0),new Uint8Array(0)))throw Error('empty array');
 return {cases,runtime:Script.runtime,arch:Process.arch,passed:true};
};'''
    script=session.create_script(code,runtime='v8');script.load()
    result=script.exports_sync.test();assert result['cases']==64 and result['arch']==('x64' if ARCH=='x64' else 'ia32')
    (work/'result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result))
finally:
    if session:session.detach()
    process.terminate();process.wait(timeout=10)
