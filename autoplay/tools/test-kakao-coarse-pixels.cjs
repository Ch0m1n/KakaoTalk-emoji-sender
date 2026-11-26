const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const source=fs.readFileSync(require('node:path').join(__dirname,'kakao-auto-bridge.js'),'utf8');
const fn=source.match(/function coarsePixels\(bytes,w,h\)\{[\s\S]*?\n\}/)[0];
const context={};vm.runInNewContext(fn,context);
const width=80,height=80,plain=new Uint8Array(72*72*3),noise=new Uint8Array(plain.length),other=new Uint8Array(plain.length);
for(let y=0;y<72;y++)for(let x=0;x<72;x++)for(let c=0;c<3;c++){
  const i=(y*72+x)*3+c,base=x<36?50:190;
  plain[i]=base;noise[i]=base+((x+y)%2?8:-8);other[i]=240-base;
}
const a=context.coarsePixels(plain,width,height),b=context.coarsePixels(noise,width,height),d=context.coarsePixels(other,width,height);
assert.equal(a.length,432);
assert.equal(Math.max(...a.map((n,i)=>Math.abs(n-b[i]))),0,'balanced fine compression noise should cancel');
assert.ok(a.reduce((sum,n,i)=>sum+Math.abs(n-d[i]),0)/a.length>100,'reversed image layout must stay distinct');
assert.ok([...context.coarsePixels(new Uint8Array(3),9,9)].every(Number.isFinite),'tiny valid image must not divide by zero');
console.log('Coarse pixel regression checks passed');
