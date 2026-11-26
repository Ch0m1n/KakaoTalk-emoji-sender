"""Local animated WebP decoding to lossless PNG frames for the GDI bridge."""
import hashlib
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent / 'deps'))
from PIL import Image


class MediaSources:
    def __init__(self, api, cache, cancelled=None):
        self.api = api
        self.cache = Path(cache)
        self.webp = {}
        self.cancelled = cancelled or (lambda:False)

    def registerfile(self, path, key):
        try:
            if hashlib.sha256(Path(path).read_bytes()).hexdigest() != key:
                return {'status': 'error', 'reason': 'source changed'}
            with Image.open(path) as image:
                if image.format not in ('GIF', 'WEBP') or not 2 <= image.n_frames <= 10000:
                    return {'status': 'unsupported'}
                if image.format == 'GIF' and image.width <= 210 and image.height <= 300 and image.n_frames <= 256:
                    result = self.api.registerfile(path, key)
                    if result['status'] not in ('unsupported', 'error'):
                        return result
                    # GDI+ has a stricter decoded-memory budget and may reject
                    # GIFs that Pillow can decode into a bounded frame pack.
                w, h = image.size
                if min(w, h) < 9 or max(w, h) > 4096 or w*h > 8_000_000:
                    return {'status': 'unsupported'}
                if w <= 210 and h <= 300:
                    target_size = (w, h)
                elif 210*h <= 300*w:
                    target_size = (210, h*210//w)
                else:
                    target_size = (w*300//h, 300)
                if min(target_size)<9:
                    return {'status': 'unsupported'}
                limit = min(900, (64*1024*1024)//(target_size[0]*target_size[1]*4))
                stride = (image.n_frames+limit-1)//limit
                playback_count = (image.n_frames+stride-1)//stride
                folder = self.cache / key
                folder.mkdir(parents=True, exist_ok=True)
                preview = folder / 'preview.png'
                image.seek(0)
                image.convert('RGBA').resize(target_size, Image.Resampling.LANCZOS).save(preview)
                count = image.n_frames
                kind = image.format
            result = self.api.registerpreview(str(preview.resolve()), key, {'frames': playback_count, 'format': kind})
            if result['status'] in ('registered', 'known'):
                self.webp[key] = {'source': path, 'folder': folder, 'count': count, 'stride': stride, 'size': (w, h), 'target_size':target_size, 'pack': None}
            return result
        except (OSError, ValueError) as error:
            return {'status': 'error', 'reason': str(error)}

    def activate(self, key):
        if self.cancelled():raise InterruptedError('Playback owner exited')
        entry = self.webp.get(key)
        if not entry:
            return self.api.activate(key)
        if entry['pack'] is None:
            if hashlib.sha256(Path(entry['source']).read_bytes()).hexdigest() != key:
                return
            paths, delays = [], []
            with Image.open(entry['source']) as image:
                if image.size != entry['size'] or image.n_frames != entry['count']:
                    raise ValueError('WebP changed before decode')
                for i in range(image.n_frames):
                    if self.cancelled():raise InterruptedError('Playback owner exited')
                    image.seek(i)
                    image.load()
                    if i % entry['stride'] == 0:
                        target = entry['folder'] / f'{i:04d}.png'
                        image.convert('RGBA').resize(entry['target_size'], Image.Resampling.LANCZOS).save(target, compress_level=1)
                        paths.append(str(target.resolve()))
                        delays.append(0)
                    delays[-1] += max(20, min(60000, int(image.info.get('duration', 100))))
            entry['pack'] = {'paths': paths, 'delays': delays}
        if self.cancelled():raise InterruptedError('Playback owner exited')
        return self.api.activate(key, entry['pack'])

    def updatepath(self, key, path):
        if key in self.webp:
            self.webp[key]['source'] = path
        self.api.updatepath(key, path)

    def unregister(self, key):
        self.webp.pop(key, None)
        self.api.unregister(key)
