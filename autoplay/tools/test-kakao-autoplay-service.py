import importlib.util
from pathlib import Path
import unittest
import io
import json
import tempfile

spec=importlib.util.spec_from_file_location('service',Path(__file__).with_name('kakao-autoplay-service.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class Worker:
    def __init__(self):self.code=None;self.stop=False;self.closed=False
    def poll(self):return self.code
    def request_stop(self):self.stop=True
    def close(self):self.closed=True

class SupervisorTests(unittest.TestCase):
    def setUp(self):
        self.now=0;self.spawned=[];self.controls=[];self.key=(100,1234,9)
        def spawn(*args):
            worker=Worker();self.spawned.append(worker);return worker
        self.s=module.Supervisor(spawn,lambda k,h:self.controls.append((k,h)),lambda:self.now)
    def tick(self,handles=(1,),alive=True,stop=False):
        self.s.tick({self.key:list(handles)} if handles else {},{self.key[:2]} if alive else set(),stop)
    def test_reopened_windows_reuse_worker(self):
        self.tick();self.tick(());self.assertEqual(self.s.phase,'waiting-for-chat')
        self.tick((2,3));self.assertEqual(len(self.spawned),1);self.assertEqual(self.controls[-1][1],[2,3])
    def test_process_restart_waits_for_old_worker(self):
        self.tick();old=self.spawned[0];self.tick(alive=False);self.assertTrue(old.stop)
        self.key=(101,5678,10);self.tick();self.assertEqual(len(self.spawned),1)
        old.code=0;self.tick();self.assertEqual(len(self.spawned),1)
        self.now=4;self.tick();self.assertEqual(len(self.spawned),2)
    def test_pid_reuse_is_different_process(self):
        self.tick();self.key=(100,9999,9);self.tick();self.assertTrue(self.spawned[0].stop)
    def test_stop_prevents_reattach(self):
        self.tick();old=self.spawned[0];self.tick(stop=True);old.code=0
        self.tick();self.now=100;self.tick();self.assertEqual(self.s.phase,'stopped');self.assertEqual(len(self.spawned),1)
    def test_worker_failure_has_retry_delay(self):
        self.tick();self.spawned[0].code=1;self.tick();self.tick();self.assertEqual(len(self.spawned),1)
        self.assertEqual(self.s.last_exit,1)
        self.now=3;self.tick();self.assertEqual(len(self.spawned),2)
    def test_no_chat_does_not_start_worker(self):
        self.tick(());self.assertEqual(self.spawned,[])

    def test_failure_is_preserved_before_worker_log_is_overwritten(self):
        previous=module.STATE
        with tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parents[1]/'artifacts/tests') as directory:
            module.STATE=Path(directory)
            try:
                (module.STATE/'bitmap-bridge.json').write_text(json.dumps({'status':'failed','error':'Bridge load failed',
                    'events':[{'type':'error','description':'ReferenceError: missing function'}],'counts':{'private-id':1}}))
                (module.STATE/'autoplay-worker.stderr.log').write_text('')
                worker=object.__new__(module.Worker)
                worker.process=type('Exited',(),{'poll':lambda self:1})()
                worker.out=io.StringIO();worker.err=io.StringIO();worker.close()
                saved=json.loads((module.STATE/'last-worker-failure.json').read_text())
                self.assertEqual(saved['exitCode'],1)
                self.assertIn('ReferenceError: missing function',saved['errors'])
                self.assertNotIn('private-id',json.dumps(saved))
                (module.STATE/'autoplay-worker.stderr.log').write_text('new worker')
                self.assertEqual(json.loads((module.STATE/'last-worker-failure.json').read_text()),saved)
            finally:module.STATE=previous

if __name__=='__main__':unittest.main()
