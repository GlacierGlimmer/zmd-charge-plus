"""Review references present in the full source, beyond short English lookup IDs.

This is a targeted semantic aid, not a claim of linguistic or visual approval.
Missing references are reported for review rather than silently discarded.
"""
import argparse
import json
from pathlib import Path
import re

from completion import read

IDENTIFIER=re.compile(r'\b[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_<>-]+)+')


def identifiers(text):
    return {m.group().rstrip('-') for m in IDENTIFIER.finditer(text) if m.group()!='e.g'}


def missing_identifiers(source,translation):
    required=identifiers(source)
    actual=identifiers(translation)
    return sorted(key for key in required if key not in actual and not any(value.startswith(key+'-') for value in actual))


def audit(repo,source):
    rows=[]
    for path in sorted((repo/'Localization/authored').glob('*.json')):
        if path.name.startswith(('manual.','review.')):continue
        pack=read(path)
        # The Chinese profile intentionally uses a fixed Chinese output variable;
        # other locales preserve the general, locale-aware input token instead.
        gaps=[dict(key=key,missing=missing_identifiers(chinese,pack[key]),source=chinese,translation=pack[key]) for key,chinese in source.items() if key!='DeepSeek {deepseek.period.name}' and key in pack and missing_identifiers(chinese,pack[key])]
        rows.append(dict(code=path.stem,coveredKeys=sum(k in pack for k in source),identifierGaps=gaps))
    return dict(distributionAuthorized=False,languageReports=rows)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo',type=Path,default=Path(__file__).resolve().parents[2])
    parser.add_argument('--source',type=Path,required=True)
    parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args();report=audit(args.repo,read(args.source))
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for row in report['languageReports']:
        print(row['code'],'covered:',row['coveredKeys'],'source identifier gaps:',len(row['identifierGaps']))
