// Release completed inference values, while preserving the warm model/session.
// Unwrap nested Tensor wrappers before disposal (some runtime bundles can wrap
// an ONNX tensor twice). Deduplicate shared input/output storage.
export function releaseTensors(...values){
    const visited=new Set(),released=new Set();
    const visit=value=>{
        if(!value||typeof value!=='object'||visited.has(value)||ArrayBuffer.isView(value)||value instanceof ArrayBuffer)return;
        visited.add(value);
        if(Array.isArray(value.dims)&&typeof value.dispose==='function'){
            let base=value;const wrappers=new Set();
            while(base.ort_tensor){
                if(base.ort_tensor===base||wrappers.has(base.ort_tensor))return;
                wrappers.add(base);base=base.ort_tensor;
            }
            if(!released.has(base)){released.add(base);try{base.dispose();}catch{}}
        }else for(const child of Object.values(value))visit(child);
    };
    values.forEach(visit);
}
