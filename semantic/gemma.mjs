import { AutoModel, AutoConfig, AutoProcessor, RawImage, env } from './transformers.min.js';
import { requestQueue } from './request-queue.mjs';
import { releaseTensors } from './tensor-memory.mjs';

// Every asset is installed locally. Requests outside this virtual host are also
// denied by the native host. Neither a query nor an image becomes a URL.
env.allowRemoteModels = false;
env.allowLocalModels = true;
env.localModelPath = './models/';
env.useBrowserCache = false;
env.backends.onnx.wasm.numThreads = 1;
env.backends.onnx.wasm.proxy = false;
env.backends.onnx.wasm.wasmPaths = {
    mjs: new URL('./ort/ort-wasm-simd-threaded.asyncify.mjs', import.meta.url).href,
    wasm: new URL('./ort/ort-wasm-simd-threaded.asyncify.wasm', import.meta.url).href,
};

const send = value => chrome.webview.postMessage(value);
const normalize = vector => {
    if (vector.length !== 768 || !vector.every(Number.isFinite)) throw Error('Invalid embedding');
    const norm = Math.hypot(...vector);
    if (!norm) throw Error('Empty embedding');
    return vector.map(value => value / norm);
};
let model, processor;
try {
    if (!navigator.gpu || !await navigator.gpu.requestAdapter()) throw Error('WebGPU unavailable');
    const config = await AutoConfig.from_pretrained('gemma2', { local_files_only: true });
    config.audio_config = null;
    model = await AutoModel.from_pretrained('gemma2', {
        config, device: 'webgpu', dtype: { model: 'q4', vision_encoder: 'q4' }, local_files_only: true,
    });
    processor = await AutoProcessor.from_pretrained('gemma2', { local_files_only: true });
    processor.image_processor.max_soft_tokens = 280;
    send({ type: 'ready' });
} catch {
    send({ type: 'fatal', error: 'EmbeddingGemma 2 q4를 시작하지 못했어요. WebGPU 지원과 모델 설치를 확인해주세요.' });
}

const receive = requestQueue(async (data, canceled) => {
            let vector;
            if (data.action === 'classify') {
                vector = await embedText('task: classification | query: ' + data.query);
            } else if (data.action === 'document') {
                vector = await embedText('title: none | text: ' + data.query);
            } else if (data.action === 'query') {
                vector = await embedText('task: search result | query: ' + data.query);
            } else if (data.action === 'image') {
                if (!Array.isArray(data.frames) || !data.frames.length || data.frames.length > 3) throw Error('Invalid frames');
                const sum = new Array(768).fill(0);
                for (const url of data.frames) {
                    if (canceled()) return;
                    if (typeof url !== 'string' || !url.startsWith('data:image/png;base64,')) throw Error('Local frame required');
                    const image = await RawImage.read(url);
                    const inputs = await processor(null, image);let result,frame;
                    try{result=await model(inputs);frame=normalize(Array.from(result.sentence_embedding.data));}
                    finally{releaseTensors(result,inputs);}
                    frame.forEach((value, index) => { sum[index] += value; });
                }
                vector = sum;
            } else throw Error('Unknown operation');
            if (!canceled()) send({ id: data.id, vector: normalize(vector) });
}, id => {
            // Deliberately omit the request, source path and exception message.
            send({ id, error: '로컬 모델 분석에 실패했어요. 파일명 검색은 계속 사용할 수 있어요.' });
});
chrome.webview.addEventListener('message', ({ data }) => receive(data));
async function embedText(text){
    const inputs=await processor([text]);let result;
    try{result=await model(inputs);return Array.from(result.sentence_embedding.data);}
    finally{releaseTensors(result,inputs);}
}
