import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('discovery', Path(__file__).with_name('kakao-gif-discovery.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class API:
    def __init__(self):
        self.sources = {}
        self.adds = 0
    def registerfile(self, path, key):
        self.sources[key] = path
        self.adds += 1
        return {'status': 'registered'}
    def unregister(self, key):
        self.sources.pop(key, None)
    def updatepath(self, key, path):
        self.sources[key] = path


class DiscoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.api = API()
        self.watch = module.GifDiscovery(directories=[self.root])
    def write(self, name, data):
        p = self.root / name
        p.write_bytes(data)
        os.utime(p, (1_700_000_000, 1_700_000_000))
        return p
    def scan(self):
        self.watch.poll(self.api)
        return self.watch.poll(self.api)
    def test_new_sources_and_deduplication(self):
        self.write('a.gif', b'GIF89a-source-one')
        self.scan()
        self.write('b.gif', b'GIF89a-source-two')
        self.write('copy.gif', b'GIF89a-source-one')
        result = self.scan()
        self.assertEqual(result['uniqueGifs'], 2)
        self.assertEqual(self.api.adds, 2)
    def test_replacement_and_deletion(self):
        a = self.write('a.gif', b'GIF89a-old')
        self.scan()
        old = set(self.api.sources)
        self.write('a.gif', b'GIF89a-new-content')
        self.scan()
        self.assertFalse(old & set(self.api.sources))
        a.unlink()
        self.scan()
        self.assertEqual(self.api.sources, {})
    def test_deleted_original_switches_to_duplicate(self):
        a = self.write('a.gif', b'GIF89a-identical')
        b = self.write('b.gif', b'GIF89a-identical')
        self.scan()
        a.unlink()
        self.scan()
        self.assertEqual(list(self.api.sources.values()), [str(b.resolve())])
    def test_partial_write_and_non_gif(self):
        self.write('a.gif', b'GIF')
        self.write('wrong.gif', b'not-a-GIF')
        self.scan()
        self.assertEqual(self.api.adds, 0)
        self.write('a.gif', b'GIF89a-complete')
        self.watch.poll(self.api)
        self.assertEqual(self.api.adds, 0)
        self.watch.poll(self.api)
        self.assertEqual(self.api.adds, 1)
    def test_batch_backlog_is_not_lost(self):
        for i in range(5):
            self.write(f'{i}.gif', b'GIF89a' + bytes([i]))
        for _ in range(5):
            self.watch.poll(self.api, batch=2)
        self.assertEqual(len(self.api.sources), 5)

if __name__ == '__main__':
    unittest.main()
