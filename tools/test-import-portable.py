import importlib.util
import json
import tempfile
from pathlib import Path

spec=importlib.util.spec_from_file_location('migration',Path(__file__).with_name('import-portable.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
with tempfile.TemporaryDirectory() as folder:
    root=Path(folder);old=root/'예전';new=root/'설치';new.mkdir()
    (old/'data/images').mkdir(parents=True)
    image=old/'data/images/콘.gif';image.write_bytes(b'original')
    original={'Entries':[{'Path':str(image),'Aliases':'고맙, 고마워요'}],'Folders':['Z:/external'],'AutoPlayOnLaunch':True}
    catalog=old/'data/catalog.json';catalog.write_text(json.dumps(original,ensure_ascii=False),encoding='utf-16')
    raw=catalog.read_bytes()
    module.migrate(old,new)
    restored=json.loads((new/'data/catalog.json').read_text('utf-8-sig'))
    assert Path(restored['Entries'][0]['Path']).resolve()==(new/'data/images/콘.gif').resolve(), (ascii(restored['Entries'][0]['Path']),ascii(str(new/'data/images/콘.gif')))
    assert restored['Entries'][0]['Aliases']=='고맙, 고마워요'
    assert restored['Folders']==['Z:/external'] and catalog.read_bytes()==raw
    assert (new/'data/images/콘.gif').read_bytes()==b'original'
    try:module.migrate(old,new)
    except ValueError:pass
    else:raise AssertionError('Existing user data overwritten')
    assert module.migrate(new,new).startswith('Existing')
print('PASS: portable import, Unicode/BOM, path relocation, external paths, source preservation, no overwrite.')
