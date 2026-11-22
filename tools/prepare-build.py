"""Prepare SDK and codec dependencies only. Does not download AI models."""
from pathlib import Path
import shutil, subprocess, sys
root=Path(__file__).resolve().parent.parent
target=root/'dist/semantic';target.mkdir(parents=True,exist_ok=True)
for name in ['prepare.py','download-integrity.json','download-sizes.json']:
    shutil.copy2(root/'semantic'/name,target/name)
subprocess.run([sys.executable,str(target/'prepare.py'),'--sdk-only'],check=True)
subprocess.run([sys.executable,'-m','pip','install','--disable-pip-version-check','--target',str(root/'dist/codecs/deps'),'Pillow==13.0.0','imageio-ffmpeg==0.8.1'],check=True)
