import tempfile
from pathlib import Path
import struct
import unittest
from unittest.mock import patch
from kakao_install import candidates, discover, machine, supported_process, x86_process


class InstallTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
    def exe(self,folder,architecture=0x14c,tail=b'arbitrary build/version'):
        path=self.root/folder/'KakaoTalk.exe';path.parent.mkdir(parents=True,exist_ok=True)
        data=bytearray(128);data[:2]=b'MZ';struct.pack_into('<I',data,60,64);data[64:68]=b'PE\0\0';struct.pack_into('<H',data,68,architecture);path.write_bytes(data+tail);return path
    def test_running_custom_path_wins(self):
        running=self.exe('custom Korean 경로');registered=self.exe('registered')
        self.assertEqual(discover(running=[running],registered=[registered],environ={}),running)
    def test_registered_quoted_icon(self):
        path=self.exe('공백 폴더');self.assertEqual(discover(running=[],registered=['"'+str(path)+'",0'],environ={}),path)
    def test_install_location(self):
        path=self.exe('app');self.assertEqual(discover(running=[],registered=[path.parent],environ={}),path)
    def test_environment_fallback(self):
        path=self.exe('programfiles/Kakao/KakaoTalk');self.assertEqual(discover(running=[],registered=[],environ={'ProgramFiles':str(self.root/'programfiles')}),path)
    def test_no_version_or_hash_gate(self):
        path=self.exe('different',tail=b'totally different executable contents');self.assertEqual(machine(path),0x14c);self.assertEqual(discover(running=[path],registered=[],environ={}),path)
    def test_absent_is_none(self):self.assertIsNone(discover(running=[],registered=[],environ={}))
    def test_bad_file_skipped(self):
        bad=self.exe('bad');bad.write_bytes(b'not a PE');good=self.exe('good');self.assertEqual(discover(running=[bad],registered=[good],environ={}),good)
    def test_dedup_and_architecture(self):
        path=self.exe('64bit',0x8664);self.assertEqual(machine(path),0x8664);self.assertEqual(list(candidates(running=[path],registered=[path],environ={})),[path])
    def test_x64_renderer_admitted_but_legacy_internal_caller_rejected(self):
        path=self.exe('64bit',0x8664)
        with patch('kakao_install.process_path',return_value=path):
            self.assertEqual(supported_process(123),path)
            with self.assertRaises(RuntimeError):x86_process(123)
    def test_qt_local_install_and_registered_directory(self):
        old=self.exe('local/Programs/Kakao/KakaoTalk/bin',0x8664)
        path=old.with_name('KakaoTalkUI.exe');old.rename(path)
        self.assertEqual(discover(running=[],registered=[],environ={'LOCALAPPDATA':str(self.root/'local')}),path)
        self.assertEqual(discover(running=[],registered=[path.parent.parent],environ={}),path)
        with patch('kakao_install.process_path',return_value=path):self.assertEqual(supported_process(123),path)

    def test_arm64_rejected(self):
        path=self.exe('arm64',0xaa64)
        with patch('kakao_install.process_path',return_value=path):
            with self.assertRaises(RuntimeError):supported_process(123)


if __name__=='__main__':unittest.main()
