import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('update',Path(__file__).with_name('kakao_update.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class UpdateTests(unittest.TestCase):
    def release(self,tag='v12.0.0',data=b'MZtest-executable'):
        return {'tag_name':tag,'draft':False,'prerelease':False,'assets':[{
            'name':m.ASSET,'state':'uploaded','size':len(data),'digest':'sha256:'+hashlib.sha256(data).hexdigest(),
            'browser_download_url':'https://github.com/'+m.REPOSITORY+'/releases/download/'+tag+'/'+m.ASSET}]}
    def test_numeric_version(self):
        self.assertGreater(m.version('v12.0.0'),m.version('11.9.9'))
        for v in ['../evil','v12.0.0-beta','12','01.0.0']:
            with self.assertRaises(ValueError):m.version(v)
    def test_new_same_and_downgrade(self):
        self.assertTrue(m.select_release(self.release(),'11.0.0')['available'])
        for current in ['12.0.0','13.0.0']:
            self.assertFalse(m.select_release(self.release(),current)['available'])
    def test_prerelease_rejected(self):
        r=self.release();r['prerelease']=True
        with self.assertRaises(ValueError):m.select_release(r,'11.0.0')
    def test_missing_and_untrusted_assets(self):
        for field,value in [('digest',None),('size',m.MAX_SIZE+1),('browser_download_url','https://evil.invalid/a.exe')]:
            r=self.release();r['assets'][0][field]=value
            with self.assertRaises(ValueError):m.select_release(r,'11.0.0')
    def test_download_and_hash_failure(self):
        data=b'MZtest-executable';info=m.select_release(self.release(data=data),'11.0.0')
        with tempfile.TemporaryDirectory() as d:
            target=m.download(info,d,lambda _:io.BytesIO(data));self.assertEqual(target.read_bytes(),data)
            with self.assertRaises(ValueError):m.download(info,d,lambda _:io.BytesIO(b'MZbad'))
            self.assertEqual(target.read_bytes(),data)
            self.assertFalse(list(Path(d).rglob('*.part')))
    def test_https_redirect_boundary(self):
        for url in ['http://github.com/a','https://github.com.evil.invalid/a','https://user@github.com/a','file:///a']:
            self.assertFalse(m.allowed_url(url))
        self.assertTrue(m.allowed_url('https://release-assets.githubusercontent.com/a'))
    def test_api_payload(self):
        self.assertTrue(m.check('11.0.0',lambda _:io.BytesIO(json.dumps(self.release()).encode()))['available'])
    def test_no_shutdown_on_bad_download(self):
        with patch.object(m,'check',return_value={'available':True}),patch.object(m,'download',side_effect=ValueError('bad hash')),patch.object(m,'prepare_handoff') as stop:
            with self.assertRaises(ValueError):m.install()
            stop.assert_not_called()
    def test_shutdown_after_download(self):
        with patch.object(m,'check',return_value={'available':True}),patch.object(m,'download',return_value=Path('new.exe')),patch.object(m,'prepare_handoff',return_value=True):
            self.assertEqual(m.install()['path'],'new.exe')
    def test_handoff_waits_for_cleanup(self):
        with tempfile.TemporaryDirectory() as d:
            state=Path(d)/'artifacts/kakao-internals';state.mkdir(parents=True)
            path=state/'autoplay-status.json';path.write_text(json.dumps({'phase':'running','managerPid':123}))
            def cleaned(_):path.write_text(json.dumps({'phase':'stopped'}))
            with patch('kakao_lifetime.creation_time',return_value=999):
                self.assertTrue(m.prepare_handoff(Path(d),pause=cleaned))
            self.assertTrue((state/'autoplay-stop.signal').exists())
    def test_handoff_timeout_blocks_switch(self):
        with tempfile.TemporaryDirectory() as d:
            state=Path(d)/'artifacts/kakao-internals';state.mkdir(parents=True)
            (state/'autoplay-status.json').write_text(json.dumps({'phase':'running','managerPid':123}))
            ticks=iter([0,26])
            with patch('kakao_lifetime.creation_time',return_value=999):
                with self.assertRaises(RuntimeError):m.prepare_handoff(Path(d),clock=lambda:next(ticks),pause=lambda _:None)

if __name__=='__main__':unittest.main()
