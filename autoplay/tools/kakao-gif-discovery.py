"""Bounded local GIF discovery; no chat databases, network or file mutations."""
import hashlib
import json
import os
import time
from pathlib import Path


class GifDiscovery:
    def __init__(self, catalog=None, directories=(), max_files=5000):
        self.catalog = Path(catalog) if catalog else None
        self.directories = [Path(p) for p in directories]
        self.max_files = max_files
        self.observed = set()
        self.priority = set()
        self.stable = {}
        self.indexed = {}
        self.registered = set()
        self.source_paths = {}
        self.rejections = 0

    def observe(self, paths, priority=False):
        for raw in paths:
            p = Path(raw)
            if p.is_absolute() and not str(p).startswith(('\\\\', '//')) and len(self.observed) < self.max_files:
                self.observed.add(p)
                if priority:
                    self.priority.add(p)

    def candidates(self):
        found = set(self.observed)
        if self.catalog and self.catalog.is_file():
            try:
                if self.catalog.stat().st_size <= 32*1024*1024:
                    data = json.loads(self.catalog.read_text(encoding='utf-8-sig'))
                    for row in data.get('Entries', [])[:self.max_files]:
                        raw = row.get('Path')
                        if raw:
                            p = Path(raw)
                            if p.suffix.lower() in ('.gif', '.webp') and not str(p).startswith(('\\\\', '//')):
                                found.add(p)
            except (OSError, ValueError, TypeError):
                # A catalog being atomically rewritten is retried on the next poll.
                pass
        for root in self.directories:
            visited = 0
            if not root.is_dir():
                continue
            for folder, dirs, names in os.walk(root, followlinks=False):
                dirs[:] = [d for d in dirs if not Path(folder, d).is_symlink()]
                for name in names:
                    visited += 1
                    if name.lower().endswith(('.gif', '.webp')):
                        found.add(Path(folder, name))
                    if visited >= self.max_files or len(found) >= self.max_files:
                        break
                if visited >= self.max_files or len(found) >= self.max_files:
                    break
        return sorted(found, key=lambda p: (p not in self.priority, p not in self.observed, str(p).casefold()))[:self.max_files]

    def poll(self, api, batch=24):
        paths = self.candidates()
        present = set(paths)
        for path in list(self.indexed):
            if path not in present or not path.is_file():
                del self.indexed[path]
        attempted = 0
        for path in paths:
            try:
                stat = path.stat()
                stamp = (stat.st_size, stat.st_mtime_ns)
                previous = self.stable.get(path)
                self.stable[path] = stamp
                if not path.is_file() or previous != stamp or time.time_ns()-stat.st_mtime_ns < 500_000_000:
                    continue
                if path in self.indexed and self.indexed[path][0] == stamp:
                    continue
                if attempted >= batch:
                    continue
                attempted += 1
                if stat.st_size > 32*1024*1024:
                    self.indexed[path] = (stamp, None)
                    self.rejections += 1
                    continue
                with path.open('rb') as stream:
                    header = stream.read(12)
                    if not (header[:6] in (b'GIF87a', b'GIF89a') or (header[:4] == b'RIFF' and header[8:12] == b'WEBP')):
                        self.indexed[path] = (stamp, None)
                        continue
                    stream.seek(0)
                    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
                # A writer could have changed the source while it was hashed.
                after = path.stat()
                if (after.st_size, after.st_mtime_ns) != stamp:
                    continue
                if digest not in self.registered:
                    result = api.registerfile(str(path.resolve()), digest)
                    if result.get('status') in ('registered', 'known'):
                        self.registered.add(digest)
                    elif result.get('status') == 'limit':
                        continue  # Retry when old references have been removed.
                    else:
                        self.rejections += 1
                        self.indexed[path] = (stamp, None)
                        continue
                self.indexed[path] = (stamp, digest)
            except (OSError, ValueError):
                continue
        active_ids = {value[1] for value in self.indexed.values() if value[1]}
        for digest in self.registered-active_ids:
            api.unregister(digest)
        self.registered.intersection_update(active_ids)
        canonical = {}
        for path, (_, digest) in self.indexed.items():
            if digest:
                canonical.setdefault(digest, str(path.resolve()))
        for digest, path in canonical.items():
            if self.source_paths.get(digest) != path:
                api.updatepath(digest, path)
        self.source_paths = canonical
        self.stable = {p: stamp for p, stamp in self.stable.items() if p in present}
        return {'candidateFiles': len(paths), 'indexedFiles': len(self.indexed), 'uniqueGifs': len(self.registered), 'rejections': self.rejections}
