"""Validate shipped files without conflating syntax with finished translation.

Use --complete for distribution. This also requires independent review and the
actual native guide for every locale in the catalog.
"""
import argparse
from pathlib import Path
from completion import BAD_TEXT, audit_repo, read, syntax_valid

ROOT=Path(__file__).resolve().parents[2]
PACKS=ROOT/'Localization'


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--complete',action='store_true')
    args=parser.parse_args()
    requirements=read(PACKS/'reviewed-release-requirements.json')
    catalog=read(PACKS/'reviewed-language-catalog.json')['languages']
    assert len(catalog)==146 and len({row['code'] for row in catalog})==146
    assert sum(bool(row.get('minecraftCode')) for row in catalog)==143
    english=read(PACKS/'en-US.json')
    assert set(requirements['requiredKeys'])<=english.keys(),'English definitions must cover the full source; values may improve without changing lookup IDs.'
    partial=[]
    for locale in catalog:
        code=locale['code'];pack=read(PACKS/(code+'.json'))
        for key in ['Settings','Display & Position','HUD Content','Data Management','Experimental Features','User Manual','About','Done','Global font size and UI scale']:
            assert isinstance(pack.get(key),str) and pack[key].strip(),(code,key,'missing navigation')
        for key,value in pack.items():
            assert isinstance(value,str) and value.strip() and not BAD_TEXT.search(value),(code,key,'invalid text')
            # Canonical manual prose is replaced by actual native guides.
            if key in requirements['requiredKeys']:
                assert syntax_valid(key,value,code,requirements),(code,key,'changed executable syntax')
        missing=set(requirements['requiredKeys'])-pack.keys()
        if missing:partial.append((code,len(missing)))
    print(f'PASS JSON, navigation and immutable syntax for {len(catalog)} shipped locale files. Partial message sets: {len(partial)}. Semantic completion is checked separately.')
    if args.complete:
        report=audit_repo(ROOT)
        failed=[row for row in report['languages'] if not row['complete']]
        assert not failed,('Translation/review incomplete',[(row['code'],len(row['missing']),len(row['english']),len(row['manual']),len(row['review'])) for row in failed])
        print('PASS every locale message, native guide and exact-file independent review.')


if __name__=='__main__':main()
