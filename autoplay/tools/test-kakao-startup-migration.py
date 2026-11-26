import importlib.util, tempfile, unittest, os, subprocess
from pathlib import Path
from unittest.mock import patch
spec=importlib.util.spec_from_file_location('launcher',Path(__file__).with_name('kakao-portable-launcher.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class StartupTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name);(self.root/'tools').mkdir()
        self.script=self.root/'tools/kakao-autoplay-service.py'
        self.script.write_text('STATE="artifacts/kakao-internals"\nMUTEX="KakaoGifAutoPlay"')
        self.cmd='"C:\\Python\\pythonw.exe" "'+str(self.script)+'"'
    def test_legacy_quoted_command_is_recognized(self):self.assertTrue(m.known_startup(self.cmd))
    def test_portable_background_is_recognized(self):
        script=self.root/'tools/kakao-portable-launcher.py';script.write_text('kakao-internals KakaoGifAutoPlay')
        self.assertTrue(m.known_startup(subprocess.list2cmdline(['C:/Python/pythonw.exe',str(script),'--background'])))
    def test_other_arguments_scripts_and_missing_files_are_rejected(self):
        self.assertFalse(m.known_startup(self.cmd+' --unrelated'))
        self.script.write_text('unrelated program');self.assertFalse(m.known_startup(self.cmd))
        self.script.unlink();self.assertFalse(m.known_startup(self.cmd))
    def test_gui_payload_after_large_resource_is_recognized(self):
        exe=self.root/'KakaoAutoPlay.exe';exe.write_bytes(b'MZ'+b'0'*(5*1024*1024)+b'payload.zip')
        self.assertTrue(m.known_startup(subprocess.list2cmdline([str(exe),'--tray','--resume'])))
        exe.write_bytes(b'MZ unrelated');self.assertFalse(m.known_startup(subprocess.list2cmdline([str(exe),'--tray','--resume'])))
    def test_startup_uses_gui_in_tray_with_resume(self):
        exe=self.root/'KakaoAutoPlay.exe';exe.write_bytes(b'MZ')
        with patch.dict(os.environ,{'KAKAO_AUTOPLAY_EXE':str(exe)}):
            self.assertEqual(m.startup_command(),subprocess.list2cmdline([str(exe),'--tray','--resume']))
    def test_migrated_running_service_waits_for_cleanup(self):
        state=self.root/'state';state.mkdir()
        (state/'startup-migration.json').write_text(m.json.dumps({'previous':self.cmd}))
        with patch.object(m,'STATE',state),patch('kakao_update.prepare_handoff') as handoff:
            m.stop_migrated_service();handoff.assert_called_once_with(self.root.resolve())
if __name__=='__main__':unittest.main()
