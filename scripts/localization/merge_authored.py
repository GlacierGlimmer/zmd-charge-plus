"""Merge structurally complete authored locales; this does not authorize export.

Existing unfinished runtime locales stay visibly incomplete in the release audit.
Regional/script variants are derived only after their base is fully authored.
Semantic and visual review of exact final files is still a separate export gate.
"""
import argparse
import json
from pathlib import Path

from completion import CHAPTERS, read, english_fallback, syntax_valid, valid_shared
from inspect_authored import inspect
from regional_variants import BRITISH, PORTUGAL, PIRATE, SHAKESPEARE, ANGLISH, LOLCAT, upside, transform_prose
from orthographic_variants import serbian, belarusian, old_indonesian, old_russian

# The catalog's de-CH is Schwiizerdütsch, a dialect rather than Swiss Standard
# German. Replacing ß with ss cannot author it; it requires its own native pack.
REGIONS={'en-AU':('en-US',BRITISH),'en-CA':('en-US',None),'en-GB':('en-US',BRITISH),'en-NZ':('en-US',BRITISH),'de-AT':('de-DE',None),'fr-CA':('fr-FR',None),'fr-CH':('fr-FR',None),'es-AR':('es-ES',None),'es-CL':('es-ES',None),'es-EC':('es-ES',None),'es-MX':('es-ES',None),'es-UY':('es-ES',None),'es-VE':('es-ES',None),'pt-PT':('pt-BR',PORTUGAL),'fil-PH':('tl-PH',None),'nl-BE':('nl-NL',None),'val-ES':('ca-ES',None)}
VARIANTS={'en-x-pirate':('en-US',PIRATE),'en-x-shakespeare':('en-US',SHAKESPEARE),'en-x-anglish':('en-US',ANGLISH),'en-x-lolcat':('en-US',LOLCAT),'en-x-upside-down':('en-US',upside),'sr-Latn':('sr-Cyrl',serbian),'be-Latn':('be-BY',belarusian),'id-x-old':('id-ID',old_indonesian),'ru-x-old':('ru-RU',old_russian)}


def save(path,value):
    # Adjacent replacement keeps readers from seeing a half-written JSON file.
    temporary=path.with_suffix(path.suffix+'.merging')
    temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    temporary.replace(path)


def merge(repo):
    directory=repo/'Localization';authored=directory/'authored'
    requirements=read(directory/'reviewed-release-requirements.json')
    report=inspect(repo)
    ready={row['code'] for row in report['languages'] if row['structuralCoverageComplete']}
    # Preserve any older locale-specific reviewed homographs when another locale
    # is merged; no completion-review hashes are created by this compiler.
    shared_path=directory/'reviewed-shared-words.json'
    shared=read(shared_path) if shared_path.is_file() else {}
    native={}
    for code in sorted(ready):
        native[code]=read(authored/(code+'.json'))
        save(directory/(code+'.json'),native[code])
        guide=authored/('manual.'+code+'.json')
        if guide.is_file():save(directory/guide.name,read(guide))
        platform_guide=authored/('manual.platform.'+code+'.json')
        if platform_guide.is_file():save(directory/platform_guide.name,read(platform_guide))
        review=authored/('review.'+code+'.json')
        shared[code]=valid_shared(read(review).get('shared',{})) if review.is_file() else {}
    derived={}
    for code,(base,transform) in {**REGIONS,**VARIANTS}.items():
        if code in ready or base not in native:continue
        def convert(text):return transform_prose(text,transform) if transform else text
        def convert_document(value):
            if isinstance(value,str):return convert(value)
            if isinstance(value,list):return [convert_document(item) for item in value]
            if isinstance(value,dict):return {key:convert_document(item) for key,item in value.items()}
            return value
        pack={key:convert(value) for key,value in native[base].items()}
        same=shared.get(base,{})
        gaps=[key for key in requirements['requiredKeys'] if key not in pack or english_fallback(key,pack[key],set(requirements['technicalStrings']),same,code in requirements['englishFamily']) or not syntax_valid(key,pack[key],code,requirements)]
        if gaps:
            derived[code]=dict(base=base,merged=False,gaps=gaps)
            continue
        save(directory/(code+'.json'),pack)
        source=directory/('manual.'+base+'.json')
        if source.is_file():
            guide={key:[convert(text) for text in value] for key,value in read(source).items()}
            save(directory/('manual.'+code+'.json'),guide)
        platform_source=directory/('manual.platform.'+base+'.json')
        if platform_source.is_file():save(directory/('manual.platform.'+code+'.json'),convert_document(read(platform_source)))
        shared[code]=same
        derived[code]=dict(base=base,merged=True)
    save(shared_path,shared)
    return dict(distributionAuthorized=False,authoredMerged=sorted(ready),derived=derived)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo',type=Path,default=Path(__file__).resolve().parents[2])
    parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args();report=merge(args.repo)
    args.report.parent.mkdir(parents=True,exist_ok=True);save(args.report,report)
    print(json.dumps(report,ensure_ascii=False))
