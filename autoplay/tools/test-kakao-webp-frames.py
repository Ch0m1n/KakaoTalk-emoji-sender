import importlib.util
from pathlib import Path
import hashlib
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('frames',Path(__file__).with_name('kakao-webp-frames.py'))
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
Image=module.Image

class API:
    def registerfile(self,path,key):return {'status':'unsupported'}
    def registerpreview(self,path,key,meta):
        self.preview=path;self.meta=meta
        return {'status':'registered'}
    def activate(self,key,pack):self.pack=pack
    def updatepath(self,*args):pass
    def unregister(self,*args):pass

class FramesTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.api=API();self.media=module.MediaSources(self.api,self.root/'cache')
    def source(self,kind,size):
        frames=[Image.new('RGBA',size,color) for color in ('red','green','blue')]
        path=self.root/('source.'+kind.lower())
        frames[0].save(path,format=kind,save_all=True,append_images=frames[1:],duration=[40,90,140],lossless=True,loop=0)
        return path,hashlib.sha256(path.read_bytes()).hexdigest()
    def test_webp_timing_and_scaled_pixels(self):
        path,key=self.source('WEBP',(600,338))
        self.assertEqual(self.media.registerfile(str(path),key)['status'],'registered')
        with Image.open(self.api.preview) as image:self.assertEqual(image.size,(210,118))
        self.media.activate(key)
        self.assertEqual(self.api.pack['delays'],[40,90,140])
        self.assertEqual(len(self.api.pack['paths']),3)
        with Image.open(self.api.pack['paths'][2]) as image:self.assertEqual(image.convert('RGB').getpixel((100,50)),(0,0,255))
    def test_tall_gif_uses_external_scaled_frames(self):
        path,key=self.source('GIF',(300,533))
        self.assertEqual(self.media.registerfile(str(path),key)['status'],'registered')
        self.assertEqual(self.api.meta['format'],'GIF')
        self.media.activate(key)
        with Image.open(self.api.pack['paths'][0]) as image:self.assertEqual(image.size,(168,300))
    def test_native_gif_rejection_falls_back_to_pillow(self):
        for status in ('unsupported','error'):
            self.api.registerfile=lambda *args:{'status':status}
            path,key=self.source('GIF',(100,120))
            self.assertEqual(self.media.registerfile(str(path),key)['status'],'registered')
            self.media.activate(key)
            self.assertEqual(self.api.pack['delays'],[40,90,140])
            self.assertEqual(self.api.meta['format'],'GIF')
    def test_native_limit_does_not_retry_or_bypass_budget(self):
        self.api.registerfile=lambda *args:{'status':'limit'}
        path,key=self.source('GIF',(100,120))
        self.assertEqual(self.media.registerfile(str(path),key)['status'],'limit')
        self.assertFalse(hasattr(self.api,'preview'))
    def test_unreadable_source_is_reported(self):
        self.assertEqual(self.media.registerfile(str(self.root/'missing.gif'),'missing')['status'],'error')
        path=self.root/'broken.gif';path.write_bytes(b'GIF89a broken')
        self.assertEqual(self.media.registerfile(str(path),hashlib.sha256(path.read_bytes()).hexdigest())['status'],'error')
    def test_changed_source_is_not_activated(self):
        path,key=self.source('WEBP',(48,40));self.media.registerfile(str(path),key)
        path.write_bytes(b'changed')
        self.media.activate(key)
        self.assertFalse(hasattr(self.api,'pack'))
    def test_width_boundary_does_not_lose_one_pixel(self):
        path,key=self.source('GIF',(326,360))
        self.assertEqual(self.media.registerfile(str(path),key)['status'],'registered')
        with Image.open(self.api.preview) as image:self.assertEqual(image.size,(210,231))
    def test_static_webp_is_not_registered(self):
        path=self.root/'static.webp';Image.new('RGB',(48,40),'red').save(path)
        result=self.media.registerfile(str(path),hashlib.sha256(path.read_bytes()).hexdigest())
        self.assertEqual(result['status'],'unsupported')
    def test_owner_exit_interrupts_frame_decode_before_activation(self):
        path,key=self.source('WEBP',(48,40));self.media.registerfile(str(path),key)
        calls=[]
        def cancelled():calls.append(1);return len(calls)>=3
        self.media.cancelled=cancelled
        with self.assertRaises(InterruptedError):self.media.activate(key)
        self.assertFalse(hasattr(self.api,'pack'))
        self.media.cancelled=lambda:False;self.media.activate(key)
        self.assertEqual(len(self.api.pack['paths']),3)
    def test_long_animation_preserves_total_duration(self):
        path=self.root/'long.gif'
        frames=[Image.new('RGB',(12,12),('red','blue')[i%2]) for i in range(1001)]
        frames[0].save(path,save_all=True,append_images=frames[1:],duration=20,loop=0)
        key=hashlib.sha256(path.read_bytes()).hexdigest()
        self.assertEqual(self.media.registerfile(str(path),key)['status'],'registered')
        self.assertEqual(self.api.meta['frames'],501)
        self.media.activate(key)
        self.assertEqual(len(self.api.pack['paths']),501)
        self.assertEqual(sum(self.api.pack['delays']),20020)
        self.assertEqual(self.api.pack['delays'][-1],20)

if __name__=='__main__':unittest.main()
