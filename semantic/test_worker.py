"""Installer integrity tests; no models, GPU, desktop UI, or network."""
import hashlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('prepare', Path(__file__).with_name('prepare.py'))
prepare = importlib.util.module_from_spec(spec)
with patch('urllib.request.urlopen', side_effect=AssertionError('network on import')):
    spec.loader.exec_module(prepare)

class Response(io.BytesIO):
    def __init__(self, data, size=None):
        super().__init__(data)
        self.headers = {'Content-Length': str(len(data) if size is None else size)}

TEST_ROOT = Path(__file__).with_name('test-work')
TEST_ROOT.mkdir(exist_ok=True)

class InstallerTests(unittest.TestCase):
    def test_pinned_model_same_size_corruption_is_repaired_without_explicit_hash(self):
        import json
        data = b'repair!'
        with tempfile.TemporaryDirectory(dir=TEST_ROOT) as directory:
            root = Path(directory)
            target = root / 'model_q4.onnx_data'
            target.write_bytes(b'corrupt')
            manifest = {'spec': prepare.SPEC, 'revision': prepare.REVISION, 'sdk': prepare.SDK,
                        'files': [{'path': 'model_q4.onnx_data', 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}]}
            (root / 'download-integrity.json').write_text(json.dumps(manifest), encoding='utf-8')
            with patch.object(prepare, 'ROOT', root), patch('urllib.request.urlopen', return_value=Response(data)) as network:
                prepare.fetch('https://example.com/model_q4.onnx_data', target)
            self.assertEqual(network.call_count, 1)
            self.assertEqual(target.read_bytes(), data)

    def test_overall_progress_and_cached_files(self):
        clock, events = [0.0], []
        progress = prepare.Progress(['a', 'b'], {'a': 1000000, 'b': 1000000}, clock=lambda: clock[0], sink=events.append)
        progress.update('a', 1000000, size=1000000, complete=True, phase='cached', force=True)
        self.assertEqual(events[-1]['percent'], 50)
        self.assertEqual(events[-1]['eta'], -1)
        clock[0] = 2
        progress.update('b', 500000, downloaded=500000, force=True)
        self.assertEqual(events[-1]['percent'], 75)
        self.assertEqual(events[-1]['remaining'], 500000)
        self.assertEqual(events[-1]['rate'], 250000)
        self.assertEqual(events[-1]['eta'], 2)
        progress.update('b', 1000000, downloaded=500000, complete=True, force=True)
        self.assertEqual(events[-1]['percent'], 99)
        progress.phase('verify')
        self.assertEqual(events[-1]['percent'], 99)
        progress.phase('done')
        self.assertEqual(events[-1]['percent'], 100)

    def test_unknown_length_does_not_invent_percentage_or_eta(self):
        events = []
        progress = prepare.Progress(['a'], {}, sink=events.append)
        progress.update('a', 1024, force=True)
        self.assertEqual(events[-1]['percent'], -1)
        self.assertEqual(events[-1]['eta'], -1)

    def test_only_q4_text_and_vision(self):
        self.assertFalse(any('audio' in n or 'fp16' in n for n in prepare.MODEL_FILES))
        self.assertEqual(len([n for n in prepare.MODEL_FILES if n.endswith('.onnx')]), 2)

    def transfer(self, data, expected=None, size=None, existing=None):
        with tempfile.TemporaryDirectory(dir=TEST_ROOT) as directory:
            target = Path(directory) / 'asset'
            if existing is not None:
                target.write_bytes(existing)
            with patch('urllib.request.urlopen', return_value=Response(data, size)):
                prepare.fetch('https://example.com/asset', target, expected)
            self.assertEqual(target.read_bytes(), data)
            self.assertFalse(target.with_name('asset.part').exists())

    def test_verified_asset_is_promoted(self):
        self.transfer(b'valid', hashlib.sha256(b'valid').hexdigest())

    def test_bad_hash_is_not_promoted(self):
        with tempfile.TemporaryDirectory(dir=TEST_ROOT) as directory:
            target = Path(directory) / 'asset'
            with patch('urllib.request.urlopen', return_value=Response(b'bad')):
                with self.assertRaises(ValueError):
                    prepare.fetch('https://example.com/asset', target, '0' * 64)
            self.assertFalse(target.exists())

    def test_short_download_is_not_promoted(self):
        with tempfile.TemporaryDirectory(dir=TEST_ROOT) as directory:
            target = Path(directory) / 'asset'
            with patch('urllib.request.urlopen', return_value=Response(b'short', 100)):
                with self.assertRaises(ValueError):
                    prepare.fetch('https://example.com/asset', target)
            self.assertFalse(target.exists())

    def test_valid_cache_is_offline(self):
        with tempfile.TemporaryDirectory(dir=TEST_ROOT) as directory:
            target = Path(directory) / 'asset'
            target.write_bytes(b'cached')
            with patch('urllib.request.urlopen', side_effect=AssertionError('unexpected download')):
                prepare.fetch('https://example.com/asset', target, hashlib.sha256(b'cached').hexdigest())

    def test_corrupt_cache_is_repaired(self):
        self.transfer(b'repaired', hashlib.sha256(b'repaired').hexdigest(), existing=b'corrupt')

if __name__ == '__main__':
    unittest.main()
