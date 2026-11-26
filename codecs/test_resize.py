"""Generated local assets only; use the exact converter/runtime being packaged."""
import hashlib
import importlib.util
from pathlib import Path
import sys
import tempfile
from PIL import Image

spec = importlib.util.spec_from_file_location("converter", sys.argv[1])
codec = importlib.util.module_from_spec(spec)
spec.loader.exec_module(codec)
with tempfile.TemporaryDirectory() as directory:
    root = Path(directory)
    for size in [(240, 120), (120, 240), (32, 24), (100, 100)]:
        source = root / "한글 정지.png"
        Image.new("RGBA", size, (220, 30, 20, 128)).save(source, dpi=(300, 72))
        before = hashlib.sha256(source.read_bytes()).digest()
        output = codec.resize_for_send(source, root / "small")
        with Image.open(output) as result:
            assert result.size == (100, 100)
            assert result.convert("RGBA").getpixel((50, 50))[3] == 128
            scale = min(1, 100 / size[0], 100 / size[1])
            box = result.getbbox()
            assert abs((box[2] - box[0]) - size[0] * scale) <= 1
            assert abs((box[3] - box[1]) - size[1] * scale) <= 1
        assert hashlib.sha256(source.read_bytes()).digest() == before
    for extension in ["gif", "webp"]:
        for loop in [None, 0, 3]:
            source = root / ("움짤." + extension)
            frames = [Image.new("RGBA", (200, 120), color) for color in ["red", "green", "blue"]]
            options = dict(save_all=True, append_images=frames[1:], duration=[80, 130, 210])
            if loop is not None:
                options["loop"] = loop
            if extension == "webp":
                options["lossless"] = True
            frames[0].save(source, **options)
            before = hashlib.sha256(source.read_bytes()).digest()
            with Image.open(source) as original:
                expected_loop = original.info.get("loop")
            output = codec.resize_for_send(source, root / "animated")
            with Image.open(output) as result:
                assert result.size == (100, 100) and result.n_frames == 3
                assert result.info.get("loop") == expected_loop
                for index, duration in enumerate([80, 130, 210]):
                    result.seek(index)
                    assert result.info["duration"] == duration
                    rgba = result.convert("RGBA")
                    assert rgba.getpixel((0, 0))[3] == 0
                    assert rgba.getpixel((50, 50))[3] == 255
            assert hashlib.sha256(source.read_bytes()).digest() == before
    source = root / "too-wide.png"
    Image.new("RGB", (2049, 1)).save(source)
    try:
        codec.resize_for_send(source, root / "rejected")
        raise AssertionError("Oversized source accepted")
    except ValueError:
        pass
print("PASS: 100x100 aspect/padding, small sources, alpha, DPI, GIF/WebP frames/delays/loops, input limits and source hashes")
