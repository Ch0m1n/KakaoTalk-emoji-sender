import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec=importlib.util.spec_from_file_location('diag',Path(__file__).with_name('kakao_diagnostics.py'))
d=importlib.util.module_from_spec(spec);spec.loader.exec_module(d)

class DiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        d.ROOT=Path(self.tmp.name);d.STATE=d.ROOT/'state';d.STATE.mkdir()
    def test_export_allowlist_and_redaction(self):
        (d.STATE/'secret.gif').write_bytes(b'private-media')
        (d.STATE/'bitmap-bridge-progress.json').write_text(json.dumps({'active':2,'bySource':{'private-id':{}},'originalDownloads':{'saved':3},'errors':['https://example.invalid/private?token=secret']}))
        d.event('test',path=str(d.ROOT/'user-file'),token='private-token')
        target=d.export()
        with zipfile.ZipFile(target) as z:
            self.assertNotIn('secret.gif',z.namelist())
            summary=json.loads(z.read('summary.json'))
            self.assertEqual(summary['playback']['active'],2)
            data=''.join(z.read(n).decode() for n in z.namelist())
            for private in ['private-media','private-id','example.invalid','private-token',str(d.ROOT)]:
                self.assertNotIn(private,data)
            json.loads(z.read('launcher-events.jsonl').decode().splitlines()[0])
    def test_qt_counts_without_media_metadata(self):
        result=d.qt_summary({'engine':'qt6-live','active':2,'loops':8,'downloadInFlight':4,'states':[
            {'album':True,'format':'webp','frames':304,'changes':609,'loops':2,'playing':True,'attempts':2,'downloadFailed':False,'url':'secret-url','originalCachePath':'secret-path'},
            {'format':'secret-format','frames':'secret-data'}],'candidates':['secret-candidate']})
        self.assertEqual(result['states'][0]['frames'],304)
        self.assertEqual(result['states'][0]['loops'],2)
        self.assertEqual(result['states'][0]['attempts'],2)
        self.assertEqual(result['downloadInFlight'],4)
        self.assertNotIn('secret',json.dumps(result))
        self.assertIsNone(d.qt_summary({'engine':'other'}))

    def test_qt_counts_without_media_metadata(self):
        result=d.qt_summary({'engine':'qt6-live','active':2,'loops':8,'downloadInFlight':4,'states':[
            {'album':True,'format':'webp','frames':304,'changes':609,'loops':2,'playing':True,'attempts':2,'downloadFailed':False,'url':'secret-url'},
            {'format':'secret-format','frames':'secret-data'}],'candidates':['secret-candidate']})
        self.assertEqual(result['states'][0]['frames'],304)
        self.assertEqual(result['states'][0]['loops'],2)
        self.assertEqual(result['states'][0]['attempts'],2)
        self.assertEqual(result['downloadInFlight'],4)
        self.assertNotIn('secret',json.dumps(result))
        self.assertIsNone(d.qt_summary({'engine':'other'}))

    def test_event_log_rotates(self):
        log=d.STATE/'launcher-events.jsonl';log.write_text('x'*270000)
        d.event('rotation')
        self.assertTrue(log.with_suffix('.jsonl.1').exists())
        self.assertLess(log.stat().st_size,1024)
    def test_empty_state_still_exports(self):
        with zipfile.ZipFile(d.export()) as z:
            self.assertIn('summary.json',z.namelist())
    def test_bitmap_cache_performance_is_numeric_and_private(self):
        (d.STATE/'bitmap-bridge-progress.json').write_text(json.dumps({
            'performancePatch':'4k-dib-cache',
            'performance':{'bitmapCacheHits':100,'calls':101,'source':'private.gif','matchMs':'private-path'}}))
        with zipfile.ZipFile(d.export()) as z:
            performance=json.loads(z.read('summary.json'))['performance']
            self.assertEqual(performance['patch'],'4k-dib-cache')
            self.assertEqual(performance['bitmapCacheHits'],100)
            self.assertNotIn('source',performance)
            self.assertNotIn('matchMs',performance)

    def test_render_dimensions_have_no_source_identity(self):
        progress={'bySource':{'private-source-a':{'width':200,'height':200},'private-source-b':{'width':200,'height':200},
                              'private-source-c':{'width':210,'height':231},'invalid':{'width':'secret','height':20}},
                  'sizes':{'200x200':10,'300x300':20,'private-file.gif':1},'windowDpi':[144,144,96,'private-title',-1],
                  'diagnosticsRevision':2}
        result=d.rendering_summary(progress)
        self.assertEqual(result['registeredSizes'],{'200x200':2,'210x231':1})
        self.assertEqual(result['observedBitmapSizes'],{'200x200':10,'300x300':20})
        self.assertEqual(result['windowDpi'],[96,144])
        self.assertNotIn('private',json.dumps(result))

    def test_export_includes_render_sizes_without_media(self):
        (d.STATE/'bitmap-bridge-progress.json').write_text(json.dumps({'bySource':{'hidden-id':{'width':200,'height':200}},
            'sizes':{'300x300':3},'windowDpi':[144],'pendingActivation':1,'failedSources':0,'maxActive':2}))
        with zipfile.ZipFile(d.export()) as z:
            summary=json.loads(z.read('summary.json'))
            self.assertEqual(summary['rendering']['windowDpi'],[144])
            self.assertEqual(summary['playback']['pendingActivation'],1)
            self.assertNotIn('hidden-id',json.dumps(summary))

    def test_partial_or_wrong_shape_render_state(self):
        self.assertEqual(d.rendering_summary({'bySource':None,'sizes':None,'windowDpi':None,'diagnosticsRevision':'private-id'}),
                         {'registeredSizes':{},'observedBitmapSizes':{},'windowDpi':[],'revision':1})

    def test_worker_failure_keeps_description_not_payload(self):
        report={'status':'failed','error':'script has been destroyed','pid':123,'counts':{'bySource':{'private-id':{}}},
                'events':[{'type':'send','payload':{'originalFile':'private-media.gif'}},
                          {'type':'error','description':'ReferenceError: diagnosticFunction is not defined'}]}
        result=d.worker_failure(report)
        self.assertEqual(result['status'],'failed')
        self.assertIn('ReferenceError: diagnosticFunction is not defined',result['errors'])
        self.assertNotIn('private',json.dumps(result))
        self.assertNotIn('pid',result)

    def test_worker_failure_redacts_file_and_url(self):
        result=d.worker_failure({'status':'failed','error':r'File error: C:\private\secret.gif'},'')
        self.assertNotIn('secret.gif',json.dumps(result))
        result=d.worker_failure({},'Traceback\nImportError: missing dependency')
        self.assertEqual(result['errors'],['ImportError: missing dependency'])

    def test_failed_worker_exports_even_without_progress(self):
        (d.STATE/'autoplay-status.json').write_text(json.dumps({'phase':'waiting','lastWorkerExit':1}))
        (d.STATE/'last-worker-failure.json').write_text(json.dumps({'status':'failed','errors':['ReferenceError: missing'],'exitCode':1}))
        with zipfile.ZipFile(d.export()) as z:
            summary=json.loads(z.read('summary.json'))
            self.assertEqual(summary['service']['lastWorkerExit'],1)
            self.assertEqual(summary['workerFailure']['errors'],['ReferenceError: missing'])

if __name__=='__main__':unittest.main()
