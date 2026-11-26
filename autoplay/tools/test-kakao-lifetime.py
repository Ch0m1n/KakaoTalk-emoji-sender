from pathlib import Path
import importlib.util,json,os,subprocess,sys,tempfile,unittest

spec=importlib.util.spec_from_file_location('lifetime',Path(__file__).with_name('kakao_lifetime.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class LifetimeTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parents[1]/'artifacts/tests');self.addCleanup(self.tmp.cleanup)
        self.state=Path(self.tmp.name)
    def write(self,value):
        (self.state/'autoplay-owner.json').write_text(json.dumps(value),encoding='utf-8-sig')
    def test_legacy_background_has_no_gui_owner(self):
        self.assertTrue(m.owner_alive(self.state,lambda pid:None));self.write(None)
        self.assertTrue(m.owner_alive(self.state,lambda pid:None))
    def test_closed_owner_and_pid_reuse_stop(self):
        self.write({'pid':12,'created':100})
        self.assertFalse(m.owner_alive(self.state,lambda pid:None))
        self.assertFalse(m.owner_alive(self.state,lambda pid:101))
        self.assertTrue(m.owner_alive(self.state,lambda pid:100))
    def test_changed_controller_is_adopted(self):
        self.write({'pid':12,'created':100});self.assertFalse(m.owner_alive(self.state,lambda pid:200))
        self.write({'pid':20,'created':200});self.assertTrue(m.owner_alive(self.state,lambda pid:200))
    def test_corrupt_owner_is_not_detached_background(self):
        (self.state/'autoplay-owner.json').write_text('{broken')
        self.assertFalse(m.owner_alive(self.state,lambda pid:100))
        for value in [{},[],{'pid':True,'created':12},{'pid':12,'created':-1}]:
            self.write(value);self.assertFalse(m.owner_alive(self.state,lambda pid:100))
    def test_explicit_login_background_can_clear_a_dead_owner(self):
        self.write({'pid':12,'created':100});m.prepare_background(self.state,lambda pid:None)
        self.assertIsNone(json.loads((self.state/'autoplay-owner.json').read_text()))
    def test_background_start_does_not_unbind_a_live_gui(self):
        self.write({'pid':12,'created':100});m.prepare_background(self.state,lambda pid:100)
        self.assertEqual(json.loads((self.state/'autoplay-owner.json').read_text(encoding='utf-8-sig')),{'pid':12,'created':100})
    def test_manager_exit_or_pid_reuse_stops_worker(self):
        environment={'KAKAO_AUTOPLAY_MANAGER_PID':'12','KAKAO_AUTOPLAY_MANAGER_CREATED':'100'}
        self.assertTrue(m.manager_alive({},lambda pid:None))
        self.assertTrue(m.manager_alive(environment,lambda pid:100))
        self.assertFalse(m.manager_alive(environment,lambda pid:101))
        self.assertFalse(m.manager_alive(environment,lambda pid:None))
    def test_actual_process_exit_is_detected(self):
        process=subprocess.Popen([sys.executable,'-c','import time; time.sleep(20)'])
        try:
            created=m.creation_time(process.pid);self.assertIsInstance(created,int)
            self.write({'pid':process.pid,'created':created});self.assertTrue(m.owner_alive(self.state))
            process.terminate();process.wait(timeout=5);self.assertFalse(m.owner_alive(self.state))
        finally:
            if process.poll() is None:process.terminate();process.wait(timeout=5)

if __name__=='__main__':unittest.main()
