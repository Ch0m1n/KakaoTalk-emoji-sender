"""Legacy installer entry point. Search runs in the local WebGPU host."""
import runpy
import sys
from pathlib import Path

if __name__ == '__main__':
    if sys.argv[1:] in (['--prepare'], ['--download-models']):
        sys.argv = [str(Path(__file__).with_name('prepare.py'))]
        runpy.run_path(sys.argv[0], run_name='__main__')
    else:
        raise SystemExit('AgitCon uses EmbeddingGemma 2 q4 WebGPU. Prepare it in the app settings.')
