"""Apply self-authored additions to the existing reviewed packs without replacing drafts."""
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]/'Localization'
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def save(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def apply():
    core=read(ROOT/'reviewed-core.json')
    for name in ['reviewed-appearance.tsv','reviewed-platform.tsv','reviewed-platform-manual.tsv','reviewed-translation-notice.tsv']:
        rows=[line.split('|') for line in (ROOT/name).read_text(encoding='utf-8').splitlines() if line and not line.startswith('#')]
        keys=next(row[1:] for row in rows if row[0]=='en-US')
        for row in rows:
            code,*values=row
            assert len(values)==len(keys),(name,code)
            path=ROOT/(code+'.json');pack=read(path)
            for key,value in zip(keys,values):
                pack[key]=value
                if code in core['order']:
                    core['messages'].setdefault(key,['']*len(core['order']))[core['order'].index(code)]=value
            save(path,pack)
    save(ROOT/'reviewed-core.json',core)

if __name__=='__main__':apply()
