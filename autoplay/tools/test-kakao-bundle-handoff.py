import importlib.util
from pathlib import Path
import tempfile,json,unittest

spec=importlib.util.spec_from_file_location('launcher',Path(__file__).with_name('kakao-portable-launcher.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class HandoffTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parents[1]/'artifacts/tests');self.addCleanup(self.tmp.cleanup)
        self.base=Path(self.tmp.name)/'KakaoAutoPlay/bundles';self.base.mkdir(parents=True)
        self.root=self.base/('a'*16);self.root.mkdir();self.old=self.base/('b'*16)
    def old_bundle(self,phase='running',updated=100):
        (self.old/'tools').mkdir(parents=True);(self.old/'runtime').mkdir();(self.old/'tools/kakao-autoplay-service.py').touch();(self.old/'runtime/pythonw.exe').touch()
        state=self.old/'artifacts/kakao-internals';state.mkdir(parents=True)
        (state/'autoplay-status.json').write_text(json.dumps({'phase':phase,'updated':updated,'managerPid':123}))
        return state
    def test_old_version_stops_before_new_can_start(self):
        state=self.old_bundle();waits=[]
        def pause(value):
            self.assertTrue((state/'autoplay-stop.signal').exists());waits.append(value)
            (state/'autoplay-status.json').write_text(json.dumps({'phase':'stopped','updated':101}))
        result=m.stop_previous_bundles(self.root,lambda:100,pause)
        self.assertEqual(result,[self.old]);self.assertEqual(len(waits),1)
    def test_busy_old_version_blocks_new_instead_of_force_killing(self):
        state=self.old_bundle()
        with self.assertRaises(RuntimeError):m.stop_previous_bundles(self.root,lambda:100,lambda value:None)
        self.assertTrue((state/'autoplay-stop.signal').exists())
    def test_stale_or_stopped_status_is_not_touched(self):
        state=self.old_bundle(updated=10)
        self.assertEqual(m.stop_previous_bundles(self.root,lambda:100,lambda value:None),[])
        self.assertFalse((state/'autoplay-stop.signal').exists())
    def test_current_root_and_unrelated_names_are_not_stopped(self):
        other=self.base/'unrelated';other.mkdir()
        self.assertEqual(m.stop_previous_bundles(self.root,lambda:100,lambda value:None),[])
        self.assertEqual(m.stop_previous_bundles(Path(self.tmp.name),lambda:100,lambda value:None),[])
    def test_corrupt_status_is_not_a_stop_target(self):
        state=self.old_bundle();(state/'autoplay-status.json').write_text('{broken')
        self.assertEqual(m.stop_previous_bundles(self.root,lambda:100,lambda value:None),[])
        self.assertFalse((state/'autoplay-stop.signal').exists())

    def registry(self,current):
        class Key:
            def __enter__(self):return self
            def __exit__(self,*args):pass
        class Registry:
            HKEY_CURRENT_USER=1;KEY_QUERY_VALUE=1;KEY_SET_VALUE=2;REG_SZ=1
            def __init__(self):self.writes=[]
            def OpenKey(self,*args):return Key()
            def QueryValueEx(self,*args):
                if current is None:raise FileNotFoundError()
                return current,1
            def SetValueEx(self,*args):self.writes.append(args[-1])
        fake=Registry();previous=m.winreg;m.winreg=fake;self.addCleanup(lambda:setattr(m,'winreg',previous));return fake
    def test_existing_startup_moves_only_from_stopped_known_bundle(self):
        command=m.subprocess.list2cmdline([str(self.old/'runtime/pythonw.exe'),str(self.old/'tools/kakao-portable-launcher.py'),'--background'])
        fake=self.registry(command);m.migrate_existing_startup([self.old])
        self.assertEqual(len(fake.writes),1);self.assertIn(str(m.PYTHON),fake.writes[0])
    def test_unrelated_startup_is_not_overwritten(self):
        fake=self.registry('unrelated-app-command')
        with self.assertRaises(RuntimeError):m.migrate_existing_startup([self.old])
        self.assertEqual(fake.writes,[])
    def test_missing_startup_is_not_enabled(self):
        fake=self.registry(None);m.migrate_existing_startup([self.old]);self.assertEqual(fake.writes,[])

if __name__=='__main__':unittest.main()
