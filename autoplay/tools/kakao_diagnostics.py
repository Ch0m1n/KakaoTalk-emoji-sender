"""Bounded local diagnostics; never exports media, URLs or chat contents."""
import json
import os
from pathlib import Path
import platform
import re
import sys
import time
import zipfile


def worker_failure(report, stderr=''):
    """Export error descriptions only; never export the full worker/media report."""
    def clean(value):
        text = str(value)[:4000]
        text = re.sub(r'[A-Za-z]:[\\/][^\r\n"<>]*', '<local-file>', text)
        text = re.sub(r'\b[0-9a-fA-F]{32,64}\b', '<id>', text)
        return redact(text)
    errors = []
    if report.get('error'):
        errors.append(clean(report['error']))
    for event in report.get('events', [])[-256:]:
        if isinstance(event, dict) and event.get('type') == 'error':
            errors.append(clean(event.get('description', 'JavaScript error')))
    if not errors and stderr.strip():
        errors.append(clean(stderr.strip().splitlines()[-1]))
    return {'status': report.get('status') if report.get('status') in ('failed','stopped','complete','starting') else 'unknown',
            'errors': list(dict.fromkeys(errors))[:8]}


def window_dpi(progress):
    """Read-only query from the helper, not a new function inside Kakao."""
    result = []
    try:
        import ctypes
        from ctypes import wintypes
        api = ctypes.WinDLL('user32').GetDpiForWindow
        api.argtypes = [wintypes.HWND]
        api.restype = wintypes.UINT
        for value in progress.get('windows', [])[:32]:
            try:
                dpi = api(int(value, 0) if isinstance(value, str) else int(value))
                if 48 <= dpi <= 768:
                    result.append(dpi)
            except (ValueError, TypeError, OverflowError):
                continue
    except (AttributeError, OSError, TypeError):
        pass
    return sorted(set(result))

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT/'artifacts/kakao-internals'

def redact(text):
    for path in (str(ROOT), str(Path.home())):
        text = re.sub(re.escape(json.dumps(path)[1:-1]), '<local-path>', text, flags=re.I)
        text = re.sub(re.escape(path), '<local-path>', text, flags=re.I)
        text = re.sub(re.escape(path.replace('\\','/')), '<local-path>', text, flags=re.I)
    text = re.sub(r'https?://[^\s"<>]+', '<url>', text, flags=re.I)
    text = re.sub(r'(?i)("(?:token|password|authorization|cookie|secret|api[_-]?key)"\s*:\s*")[^"]*(")', r'\1<redacted>\2', text)
    text = re.sub(r'(?i)\b(token|password|authorization|cookie|secret|api[_-]?key)(\s*[:=]\s*)[^\s,;"\x27]+', r'\1\2<redacted>', text)
    text = re.sub(r'(?i)\bBearer\s+[^\s"\x27]+', 'Bearer <redacted>', text)
    return text

def event(kind, **fields):
    STATE.mkdir(parents=True, exist_ok=True)
    path = STATE/'launcher-events.jsonl'
    if path.exists() and path.stat().st_size > 256*1024:
        backup = path.with_suffix('.jsonl.1')
        if backup.exists():
            path.with_suffix('.jsonl.2').write_bytes(backup.read_bytes())
        backup.write_bytes(path.read_bytes())
        path.write_text('', encoding='utf-8')
    record = {'time':time.strftime('%Y-%m-%dT%H:%M:%S%z'), 'event':kind, **fields}
    with path.open('a', encoding='utf-8') as stream:
        stream.write(redact(json.dumps(record, ensure_ascii=False))+'\n')

def read_json(name):
    try:
        return json.loads((STATE/name).read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return {}


def size_counts(values):
    """Only image dimensions/counters; never source IDs, paths or captions."""
    if not isinstance(values, dict):
        return {}
    return {key: count for key, count in list(values.items())[:100]
            if isinstance(key, str) and re.fullmatch(r'[1-9][0-9]{0,3}x[1-9][0-9]{0,3}', key)
            and isinstance(count, int) and not isinstance(count, bool) and count >= 0}


def rendering_summary(progress):
    counts = {}
    sources = progress.get('bySource', {})
    for source in (sources.values() if isinstance(sources, dict) else []):
        if not isinstance(source, dict):
            continue
        w, h = source.get('width'), source.get('height')
        if all(isinstance(n, int) and not isinstance(n, bool) and 0 < n <= 4096 for n in (w, h)):
            key = f'{w}x{h}'
            counts[key] = counts.get(key, 0) + 1
    dpis = progress.get('windowDpi', [])
    if not isinstance(dpis, list):
        dpis = []
    revision = progress.get('diagnosticsRevision', 1)
    if not isinstance(revision, int) or isinstance(revision, bool) or not 1 <= revision <= 100:
        revision = 1
    return {'registeredSizes': size_counts(counts), 'observedBitmapSizes': size_counts(progress.get('sizes', {})),
            'windowDpi': sorted({d for d in dpis if isinstance(d, int) and not isinstance(d, bool) and 48 <= d <= 768})[:16],
            'revision': revision}

def qt_summary(progress):
    if progress.get('engine') != 'qt6-live':return None
    def number(value):return value if isinstance(value,(int,float)) and not isinstance(value,bool) and 0<=value<1e15 else None
    rows=progress.get('states',[])
    return {'engine':'qt6-live',**{k:number(progress.get(k)) for k in ('active','created','frameChanges','loops','scanMs','scanned','downloadPending','downloadInFlight')},
            'states':[{'album':row.get('album') is True,'playing':row.get('playing') is True,
                       'format':row.get('format') if row.get('format') in ('gif','webp') else None,
                       **{k:number(row.get(k)) for k in ('frames','changes','loops','originalStatus','downloadStatus','attempts')},'downloadFailed':row.get('downloadFailed') is True}
                      for row in (rows if isinstance(rows,list) else [])[:8] if isinstance(row,dict)]}


def export(destination=None):
    STATE.mkdir(parents=True, exist_ok=True)
    target = Path(destination) if destination else STATE/('KakaoAutoPlay-diagnostics-'+time.strftime('%Y%m%d-%H%M%S')+'.zip')
    target.parent.mkdir(parents=True, exist_ok=True)
    status = read_json('autoplay-status.json')
    progress = read_json('bitmap-bridge-progress.json')
    if 'windowDpi' not in progress:
        progress['windowDpi'] = window_dpi(progress)
    downloads = progress.get('originalDownloads', {})
    summary = {
        'diagnosticsVersion':10,
        'created':time.strftime('%Y-%m-%dT%H:%M:%S%z'),
        'environment':{'os':platform.system(), 'release':platform.release(), 'version':platform.version(),
                       'machine':platform.machine(), 'python':sys.version.split()[0], 'bundledRuntime':str(ROOT) in sys.executable},
        'service':{k:status.get(k) for k in ['phase','generation','supportedVersion','updated','unlimited','lastWorkerExit']},
        'workerFailure':read_json('last-worker-failure.json') or worker_failure(read_json('bitmap-bridge.json')),
        'playback':{k:progress.get(k) for k in ['rendererVersion','refreshPolicy','refreshRequests','refreshSkipped','registered','active','maxActive','matches','ambiguous','matchErrors','pendingActivation','failedSources','croppedDraws','sourceRectMismatch','replacements','errors','rejected','decodedBytes']},
        'rendering':rendering_summary(progress),
        'qt':qt_summary(progress),
        'performance':{
            'architecture':progress.get('architecture') if progress.get('architecture') in ('ia32','x64') else None,
            'patch':progress.get('performancePatch') if progress.get('performancePatch') in ('4k-perf-v1','4k-dib-cache') else None,
            'maxRefreshFps':progress.get('maxRefreshFps'),
            **{key:value for key,value in progress.get('performance',{}).items()
               if key in ('calls','matchMs','fromBitmapMs','normalizeMs','chooseMs','bitmapCacheHits') and
               isinstance(value,(int,float)) and not isinstance(value,bool) and 0<=value<1e15}},
        'downloads':{k:downloads.get(k) for k in ['available','reason','created','saved','requested','backfilled','backfilledFormats','concurrency','maxInFlight','pending','errors']},
        'privacy':'Media, source identities, window titles, message contents and full URLs are not included.'}
    allowed = ['launcher-events.jsonl','launcher-events.jsonl.1','launcher-events.jsonl.2',
               'portable-error.txt','autoplay-error.json','autoplay-worker.stderr.log']
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('summary.json', redact(json.dumps(summary,ensure_ascii=False,indent=2)))
        for name in allowed:
            path = STATE/name
            try:
                with path.open('rb') as stream:
                    stream.seek(max(0,path.stat().st_size-64*1024))
                    text = stream.read(64*1024).decode('utf-8', errors='replace')
                archive.writestr(name, redact(text))
            except OSError:
                continue
    event('diagnostics_exported', filename=target.name)
    return str(target)
