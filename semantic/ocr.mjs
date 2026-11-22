let preparing;
const stage=text=>chrome.webview.postMessage({type:"ocr-stage",text});
async function engine() {
    if (!preparing) preparing = (async () => {
        stage("loading local OCR script");
        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = new URL('./ocr/tesseract.min.js', import.meta.url).href;
            script.onload = resolve;
            script.onerror = () => reject(Error('Local OCR unavailable'));
            document.head.append(script);
        });
        const base = new URL('./ocr/', import.meta.url);
        stage("starting local OCR worker");
        const worker = await Tesseract.createWorker('kor', 1, {
            workerPath: new URL('worker.min.js', base).href,
            corePath: base.href,
            langPath: base.href.replace(/\/$/, ''),
            workerBlobURL: false, cacheMethod: 'none', gzip: true,
            logger: message=>stage(message.status),
        });
        await worker.setParameters({ tessedit_pageseg_mode: '11', preserve_interword_spaces: '1', user_defined_dpi: '300' });
        stage("local OCR ready");
        return worker;
    })();
    return preparing;
}
export async function stickerText(frames, canceled = () => false) {
    const worker = await engine();
    let best = { text: '', confidence: 0 };
    for (const frame of frames.slice(0, 3)) {
        if (canceled()) return best;
        stage('decoding sticker bitmap');
        const image = await createImageBitmap(await (await fetch(frame)).blob());
        stage('sticker bitmap decoded');
        const canvas = document.createElement('canvas');
        const scale = Math.min(4, 1000 / Math.max(image.width, image.height));
        canvas.width = Math.round(image.width * scale);
        canvas.height = Math.round(image.height * scale);
        const context = canvas.getContext('2d');
        context.fillStyle = 'white'; context.fillRect(0, 0, canvas.width, canvas.height);
        context.drawImage(image, 0, 0, canvas.width, canvas.height);
        stage("recognizing sticker text");
        const regions=[];
        await worker.setParameters({tessedit_pageseg_mode:'11'});
        const full=await worker.recognize(canvas);
        regions.push({text:full.data.text.trim().slice(0,500),confidence:full.data.confidence});
        const header=document.createElement('canvas');header.width=canvas.width;header.height=Math.round(canvas.height*.34);
        const headerContext=header.getContext('2d');headerContext.drawImage(canvas,0,0);
        const pixels=headerContext.getImageData(0,0,header.width,header.height);
        for(let i=0;i<pixels.data.length;i+=4){const value=(pixels.data[i]+pixels.data[i+1]+pixels.data[i+2])/3<155?0:255;pixels.data[i]=pixels.data[i+1]=pixels.data[i+2]=value;}
        headerContext.putImageData(pixels,0,0);
        await worker.setParameters({tessedit_pageseg_mode:'6'});
        const top=await worker.recognize(header);
        regions.push({text:top.data.text.trim().slice(0,200),confidence:top.data.confidence});
        image.close();
        if(!best.regions)best.regions=[];
        best.regions.push(...regions.filter(region=>region.text));
        for(const region of regions)if(region.text&&region.confidence>=best.confidence){best.text=region.text;best.confidence=region.confidence;}
    }
    return best;
}
