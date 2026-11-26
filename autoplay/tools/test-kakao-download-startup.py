import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location('startup',Path(__file__).with_name('kakao_download_startup.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class Script:
    def __init__(self,error=None,seed_error=None):
        self.error=error;self.seed_error=seed_error;self.seeded=0;self.unloaded=False;self.exports_sync=self
    def on(self,kind,callback):self.message=callback
    def load(self):
        if self.error:self.message({'type':'error','description':self.error},None)
    def seedexisting(self):
        self.seeded+=1
        if self.seed_error:raise RuntimeError(self.seed_error)
    def unload(self):self.unloaded=True

class StartupTests(unittest.TestCase):
    def test_guard_failure_does_not_call_missing_rpc_or_stop_renderer(self):
        s=Script('Error: Unexpected download entry rva=0xeb9b30 expected=0x55 actual=0xe9')
        messages=[];ok,reason=m.start_downloader(s,lambda value,data:messages.append(value))
        self.assertFalse(ok);self.assertTrue(s.unloaded);self.assertEqual(s.seeded,0)
        self.assertIn('Unexpected download entry',reason);self.assertEqual(len(messages),1)
    def test_success_keeps_downloader_loaded(self):
        s=Script();self.assertEqual(m.start_downloader(s,lambda *args:None),(True,None))
        self.assertEqual(s.seeded,1);self.assertFalse(s.unloaded)
    def test_unsupported_base_is_safe_local_fallback(self):
        s=Script('Error: Unsupported image');self.assertFalse(m.start_downloader(s,lambda *args:None)[0])
        self.assertTrue(s.unloaded)
    def test_unknown_failure_is_not_hidden_or_unsafely_unloaded(self):
        s=Script(seed_error='Unexpected native cleanup failure')
        with self.assertRaises(RuntimeError):m.start_downloader(s,lambda *args:None)
        self.assertFalse(s.unloaded)

if __name__=='__main__':unittest.main()
