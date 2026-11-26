"""Explicit, transactional portable-data import. Source files are never modified."""
import json
import shutil
import sys
import uuid
from pathlib import Path


def copy_tree(source, target):
    if source.is_symlink() or getattr(source.stat(), 'st_file_attributes', 0) & 0x400:
        raise ValueError('Linked source folders cannot be imported')
    target.mkdir(parents=True)
    for item in source.iterdir():
        if item.is_symlink() or getattr(item.stat(), 'st_file_attributes', 0) & 0x400:
            raise ValueError('Linked files cannot be imported')
        if item.is_dir():
            copy_tree(item, target/item.name)
        else:
            shutil.copy2(item, target/item.name)


def migrate(source, target):
    source, target = Path(source).resolve(), Path(target).resolve()
    if source == target:
        return 'Existing installation kept in place.'
    data = source/'data'
    if not (data/'catalog.json').is_file():
        raise ValueError('Source catalog is missing')
    if (target/'data').exists():
        raise ValueError('Destination already has user data; no files were overwritten')
    if source in target.parents or target in source.parents:
        raise ValueError('Nested source/destination refused')
    # Read and validate before copying anything; detect the encodings supported by Catalog.Load.
    raw = (data/'catalog.json').read_bytes()
    encoding = 'utf-16' if raw[:2] in (b'\xff\xfe', b'\xfe\xff') else 'utf-8-sig'
    catalog = json.loads(raw.decode(encoding))
    if not isinstance(catalog, dict):
        raise ValueError('Invalid catalog')
    prefix = str(source).rstrip('\\/') + '\\'
    def rewrite(value):
        if isinstance(value, dict):
            return {key: rewrite(item) for key, item in value.items()}
        if isinstance(value, list):
            return [rewrite(item) for item in value]
        if isinstance(value, str) and value.replace('/', '\\').lower().startswith(prefix.lower()):
            return str(target/value[len(prefix):].replace('\\', '/'))
        return value
    stage = target/('.import-'+uuid.uuid4().hex)
    try:
        copy_tree(data, stage)
        (stage/'catalog.json').write_text(json.dumps(rewrite(catalog), ensure_ascii=False), encoding='utf-8-sig')
        stage.rename(target/'data')
    finally:
        if stage.exists():
            shutil.rmtree(stage)
    return 'Imported catalog, aliases and downloaded media. Original folder was preserved. Models remain in the original folder and can be prepared again.'


if __name__ == '__main__':
    try:
        message = migrate(sys.argv[1], sys.argv[2])
    except Exception as error:
        message = str(error)
        status = 1
    else:
        status = 0
    (Path(sys.argv[2])/'migration-result.txt').write_text(message, encoding='utf-8-sig')
    raise SystemExit(status)
