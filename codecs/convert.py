"""Decode only local cached sticker bytes; never fetch a URL."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent / "deps"))


def convert(source, output_base):
    try:
        from PIL import Image
    except ImportError as error:
        raise RuntimeError("이미지 변환 구성요소 Pillow를 불러오지 못했어요. 동봉된 runtime과 codecs 폴더를 함께 교체해주세요.") from error
    source, output_base = Path(source), Path(output_base)
    header = source.read_bytes()[:16]
    if header[:4] == b"RIFF" and header[8:12] == b"WEBP":
        with Image.open(source) as image:
            frames, durations = [], []
            count = getattr(image, "n_frames", 1)
            if image.width > 2048 or image.height > 2048 or count > 900 or image.width * image.height * count > 40_000_000:
                raise ValueError("아카콘의 해상도 또는 프레임 수가 너무 커요")
            loop = image.info.get("loop", 0)
            for index in range(count):
                image.seek(index)
                frames.append(image.convert("RGBA"))
                durations.append(max(10, int(image.info.get("duration", 100))))
            if sum(durations) > 30_000:
                raise ValueError("30초 이하의 움직이는 아카콘만 변환해요")
            if count == 1:
                output = output_base.with_suffix(".png")
                frames[0].save(output, format="PNG")
            else:
                output = output_base.with_suffix(".gif")
                frames[0].save(output, format="GIF", save_all=True, append_images=frames[1:], duration=durations, loop=loop, disposal=2, optimize=False)
            return output
    if header[4:8] == b"ftyp" or header[:4] == bytes([0x1A, 0x45, 0xDF, 0xA3]):
        import imageio_ffmpeg
        binaries = Path(imageio_ffmpeg.__file__).parent / "binaries"
        exe = next(binaries.glob("ffmpeg*.exe"))
        base = [str(exe), "-nostdin", "-hide_banner", "-protocol_whitelist", "file,pipe", "-i", str(source)]
        options = {"capture_output": True, "creationflags": getattr(subprocess, "CREATE_NO_WINDOW", 0)}
        probe = subprocess.run(base, timeout=15, **options)
        text = probe.stderr.decode("utf-8", errors="replace")
        duration = re.search(r"Duration:\s*(\d+):(\d+):([\d.]+)", text)
        size = re.search(r"Video:.*?\b(\d{2,5})x(\d{2,5})\b", text)
        if not duration or not size:
            raise ValueError("영상형 아카콘의 길이와 해상도를 확인하지 못했어요")
        seconds = int(duration[1]) * 3600 + int(duration[2]) * 60 + float(duration[3])
        if seconds <= 0 or seconds > 30 or int(size[1]) > 2048 or int(size[2]) > 2048:
            raise ValueError("30초 이하, 2048 픽셀 이하 영상형 콘만 변환해요")
        output = output_base.with_suffix(".gif")
        filters = "fps=20,scale=w='min(512,iw)':h='min(512,ih)':force_original_aspect_ratio=decrease,split[a][b];[a]palettegen=reserve_transparent=1[p];[b][p]paletteuse"
        subprocess.run(base + ["-an", "-filter_complex", filters, "-loop", "0", "-y", str(output)], timeout=60, check=True, **options)
        return output
    raise ValueError("지원하지 않는 아카콘 형식이에요")


def resize_for_send(source, output_base):
    from PIL import Image
    source, output_base = Path(source), Path(output_base)
    if source.stat().st_size > 30 * 1024 * 1024:
        raise ValueError("30MB 이하 이미지만 축소해요")
    with Image.open(source) as image:
        count = getattr(image, "n_frames", 1)
        if image.width > 2048 or image.height > 2048 or count > 900 or image.width * image.height * count > 40_000_000:
            raise ValueError("이미지 해상도 또는 프레임 수가 너무 커요")
        loop = image.info.get("loop")
        frames, durations = [], []
        for index in range(count):
            image.seek(index)
            frame = image.convert("RGBA")
            frame.thumbnail((100, 100), Image.Resampling.LANCZOS)
            canvas = Image.new("RGBA", (100, 100))
            canvas.alpha_composite(frame, ((100 - frame.width) // 2, (100 - frame.height) // 2))
            if count > 1:
                palette = canvas.convert("RGB").quantize(colors=255)
                transparent = canvas.getchannel("A").point(lambda alpha: 255 if alpha < 128 else 0)
                palette.paste(255, mask=transparent)
                palette.info["transparency"] = 255
                frames.append(palette)
            else:
                frames.append(canvas)
            durations.append(max(0, int(image.info.get("duration", 100))))
        output = output_base.with_suffix(".gif" if count > 1 else ".png")
        if count > 1:
            options = dict(save_all=True, append_images=frames[1:], duration=durations, disposal=2, transparency=255, optimize=False)
            if loop is not None:
                options["loop"] = loop
            frames[0].save(output, format="GIF", **options)
        else:
            frames[0].save(output, format="PNG", dpi=(96, 96))
        return output


if __name__ == "__main__":
    try:
        result = (resize_for_send if "--100" in sys.argv[3:] else convert)(sys.argv[1], sys.argv[2])
        print(json.dumps({"path": str(result)}), flush=True)
    except Exception as error:
        print(json.dumps({"error": str(error)}, ensure_ascii=False), flush=True)
        sys.exit(1)
