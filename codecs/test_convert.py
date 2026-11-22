import importlib.util
import pathlib
import subprocess
import sys
import tempfile
import unittest

root = pathlib.Path(__file__).resolve().parents[1] / "dist" / "codecs"
sys.path.insert(0, str(root / "deps"))
from PIL import Image
import imageio_ffmpeg

spec = importlib.util.spec_from_file_location("codec", root / "convert.py")
codec = importlib.util.module_from_spec(spec)
spec.loader.exec_module(codec)


class CodecTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = pathlib.Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_static_webp_alpha(self):
        source = self.path / "static.webp"
        Image.new("RGBA", (16, 16), (50, 80, 100, 64)).save(source, "WEBP", lossless=True)
        output = codec.convert(source, self.path / "output")
        self.assertEqual(output.suffix, ".png")
        with Image.open(output) as result:
            self.assertEqual(result.convert("RGBA").getpixel((0, 0)), (50, 80, 100, 64))

    def test_animated_webp_not_flattened(self):
        source = self.path / "animated.webp"
        a, b = Image.new("RGBA", (16, 16), "red"), Image.new("RGBA", (16, 16), "green")
        a.save(source, "WEBP", save_all=True, append_images=[b], duration=[80, 120], loop=0, lossless=True)
        output = codec.convert(source, self.path / "output")
        self.assertEqual(output.suffix, ".gif")
        with Image.open(output) as result:
            self.assertEqual(result.n_frames, 2)
            self.assertEqual(result.info["loop"], 0)
            self.assertEqual(result.info["duration"], 80)
            first = result.convert("RGB").getpixel((0, 0))
            result.seek(1)
            self.assertEqual(result.info["duration"], 120)
            self.assertNotEqual(first, result.convert("RGB").getpixel((0, 0)))

    def test_mp4_produces_animated_gif(self):
        exe = next((pathlib.Path(imageio_ffmpeg.__file__).parent / "binaries").glob("ffmpeg*.exe"))
        source = self.path / "movie.mp4"
        subprocess.run([str(exe), "-v", "error", "-f", "lavfi", "-i", "testsrc=size=32x32:rate=10:duration=0.5", "-pix_fmt", "yuv420p", "-y", str(source)], check=True, capture_output=True)
        output = codec.convert(source, self.path / "output")
        with Image.open(output) as result:
            self.assertGreater(result.n_frames, 1)
            self.assertEqual(result.info["loop"], 0)

    def test_unsupported_bytes_rejected(self):
        source = self.path / "other"
        source.write_bytes(b"not an image")
        with self.assertRaises(ValueError):
            codec.convert(source, self.path / "output")


if __name__ == "__main__":
    unittest.main()
