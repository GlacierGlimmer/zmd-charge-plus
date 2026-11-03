"""Find copied English clauses inside otherwise translated messages and guides."""
import argparse,json,re
from pathlib import Path
from completion import SLOTS,read

def fragments(text,size=6):
    # Mark opaque spans as boundaries; deleting them must not join unrelated
    # prose into a false copied clause. Enum values and function lists are code.
    text=SLOTS.sub('\n',text)
    text=re.sub(r'\b[A-Z][A-Z0-9_]{1,}\b','\n',text)
    text=re.sub(r'\b[a-z_]+\([^()]*\)','\n',text)
    text=re.sub(r'\b[a-z_]+(?:/[a-z_]+){2,}\b','\n',text)
    result=set()
    for part in text.splitlines():
        words=re.findall(r"[A-Za-z]+(?:['’][A-Za-z]+)?",part)
        result.update(' '.join(words[i:i+size]).casefold() for i in range(len(words)-size+1))
    return result

def overlap(source,value):return sorted(fragments(source)&fragments(value))

def review(repo,source):
    root=repo/'Localization/authored';reports=[]
    requirements=read(repo/'Localization/reviewed-release-requirements.json')
    native_source=read(root/'manual.platform.en-US.json')
    for path in sorted(root.glob('*.json')):
        if path.name.startswith(('manual.','review.')) or path.stem in requirements['englishFamily']:continue
        pack=read(path);findings=[]
        for key in source:
            if key in pack and (shared:=overlap(key,pack[key])):
                findings.append(dict(kind='message',key=key,fragments=shared,value=pack[key]))
        native=root/('manual.platform.'+path.name)
        if native.is_file():
            doc=read(native)
            for platform in ['linux','macos','android']:
                for chapter,values in doc[platform].items():
                    for index,value in enumerate(values):
                        if shared:=overlap(native_source[platform][chapter][index],value):
                            findings.append(dict(kind='native-manual',platform=platform,chapter=chapter,part=index,fragments=shared,value=value))
        reports.append(dict(code=path.stem,findings=findings))
    return dict(distributionAuthorized=False,findingsRequireContextualReview=True,languages=reports)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--repo',type=Path,default=Path(__file__).resolve().parents[2]);parser.add_argument('--source',type=Path,required=True);parser.add_argument('--report',type=Path,required=True)
    a=parser.parse_args();r=review(a.repo,read(a.source));a.report.parent.mkdir(parents=True,exist_ok=True);a.report.write_text(json.dumps(r,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({row['code']:len(row['findings']) for row in r['languages']},ensure_ascii=False))
