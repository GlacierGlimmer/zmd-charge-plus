"""Compile authored additions without treating fallback English as reviewed translation.

The coverage report is deliberately separate from catalog coverage: a selectable
locale is not evidence that every setting and guide paragraph was translated.
"""
import json
from pathlib import Path
from regional_variants import build as build_regions
from orthographic_variants import build as build_scripts
from apply_additions import apply as apply_additions
from apply_screenshot_review import apply as apply_screenshot_review

ROOT=Path(__file__).resolve().parents[2]
PACKS=ROOT/'Localization'
KEYS=['Settings','Display & Position','HUD Content','Data Management','Experimental Features','User Manual','About','Variable Library','Hardware Data','Data Source Configuration','Save & Apply','Follow system','Done','Cancel','Search','Global font size and UI scale','Font scale','UI scale','Reset to 100%']

def read(path):return json.loads(path.read_text(encoding='utf-8'))
def save(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def build():
    apply_additions()
    catalog=read(PACKS/'reviewed-language-catalog.json')['languages']
    source=read(PACKS/'en-US.json')
    authored={}
    for line in (PACKS/'reviewed-expanded-menu.tsv').read_text(encoding='utf-8').splitlines():
        if not line or line.startswith('#'):continue
        code,*values=line.split('|')
        assert len(values)==len(KEYS),(code,len(values))
        assert code not in authored,code
        authored[code]=dict(zip(KEYS,values))
    for item in catalog:
        code=item['code']
        if code not in authored:continue
        reviewed=authored[code]
        reviewed['Language']=reviewed['Interface language']=item['languageWord']
        reviewed['Search languages, regions or language codes']=reviewed['Search']+' · '+item['languageWord']+' / '+code
        for old,current in [('Variables','Variable Library'),('Data Sources','Data Source Configuration'),('HUD Content & Data','HUD Content'),('Save and apply','Save & Apply')]:reviewed[old]=reviewed[current]
        # Only authored fields are written. Runtime fallback remains distinguishable
        # in audits and cannot accidentally inflate the native coverage count.
        path=PACKS/(code+'.json')
        pack=read(path) if path.exists() else {}
        pack.update(reviewed)
        save(path,pack)
    regions,variants=build_regions(PACKS)
    scripts=build_scripts(PACKS)
    apply_screenshot_review()
    report=[]
    for item in catalog:
        code=item['code'];path=PACKS/(code+'.json')
        pack=read(path) if path.exists() else {}
        untranslated=[key for key in source if key not in pack]
        report.append({'code':code,'catalogEntry':True,'bundled':path.exists(),'authoredMessages':len(pack),'sourceMessages':len(source),'missingMessages':len(untranslated),'completeCatalog':not untranslated,'regionalBase':regions.get(code),'styledEnglish':code in variants})
    save(PACKS/'reviewed-coverage.json',{'languages':report,'completeLocaleCount':sum(r['completeCatalog'] for r in report),'catalogLocaleCount':len(catalog)})
    print('Catalog:',len(catalog),'Bundled:',sum(r['bundled'] for r in report),'Complete message sets:',sum(r['completeCatalog'] for r in report))

if __name__=='__main__':build()
