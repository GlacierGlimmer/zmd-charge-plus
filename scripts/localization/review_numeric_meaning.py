"""Flag omitted source constants for semantic review; never rewrite translated prose.

Numbers may legitimately be written as words or combined with a unit. Findings
therefore need a person to compare their full context rather than being treated
as a mechanical failure or as automatic permission to export.
"""
import argparse,json,re,unicodedata
from pathlib import Path
from completion import SLOTS,read

NUMBER=re.compile(r'(?<![A-Za-z0-9_])\d+(?:[.,]\d+)?(?![A-Za-z0-9_]|\.\d)')

def constants(text):
    text=''.join(str(unicodedata.decimal(c)) if c.isdecimal() else c for c in SLOTS.sub('',text)).replace('٫','.')
    text=re.sub(r'(\d)(h|ms|s|bit)\b',r'\1 \2',text)
    return {value.replace(',','.') for value in NUMBER.findall(text)}

def review(repo,source):
    reports=[]
    for path in sorted((repo/'Localization/authored').glob('*.json')):
        if path.name.startswith(('manual.','review.')):continue
        pack=read(path);gaps=[]
        for key,chinese in source.items():
            if key not in pack:continue
            missing=constants(chinese)-constants(pack[key])
            if missing:gaps.append(dict(key=key,constants=sorted(missing),source=chinese,translation=pack[key]))
        reports.append(dict(code=path.stem,findings=gaps))
    return dict(distributionAuthorized=False,findingsRequireContextualReview=True,languages=reports)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo',type=Path,default=Path(__file__).resolve().parents[2])
    parser.add_argument('--source',type=Path,required=True)
    parser.add_argument('--report',type=Path,required=True)
    a=parser.parse_args();result=review(a.repo,read(a.source))
    a.report.parent.mkdir(parents=True,exist_ok=True)
    a.report.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({row['code']:len(row['findings']) for row in result['languages']},ensure_ascii=False))
