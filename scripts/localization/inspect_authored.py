"""Report real translation work in progress. This never authorizes distribution."""
import argparse
import json
from pathlib import Path

from completion import BAD_TEXT, CHAPTERS, english_fallback, read, syntax_valid, valid_shared


def inspect(repo):
    directory=repo/'Localization'
    requirements=read(directory/'reviewed-release-requirements.json')
    result=[]
    for code in requirements['localeCodes']:
        path=directory/'authored'/(code+'.json')
        if not path.is_file(): continue
        pack=read(path)
        review_path=directory/'authored'/('review.'+code+'.json')
        shared=valid_shared(read(review_path).get('shared',{})) if review_path.is_file() else {}
        missing=[]; english=[]; syntax=[]; malformed=[]
        for key in requirements['requiredKeys']:
            value=pack.get(key)
            if not isinstance(value,str) or not value.strip():missing.append(key);continue
            if BAD_TEXT.search(value):malformed.append(key)
            if english_fallback(key,value,set(requirements['technicalStrings']),shared,code in requirements['englishFamily']):english.append(key)
            if not syntax_valid(key,value,code,requirements):syntax.append(key)
        guide=directory/'authored'/('manual.'+code+'.json')
        guide_structural=False
        if guide.is_file():
            manual=read(guide)
            guide_structural=set(manual)==set(CHAPTERS+['_labels']) and len(manual['_labels'])==5 and all(len(manual[c])==4 and len(manual[c][2].splitlines())>=3 and all(manual[c]) for c in CHAPTERS)
        canonical=code in requirements['englishFamily'] or code in {'zh-CN','zh-TW','zh-HK'}
        result.append(dict(code=code,messages=len(pack),missing=missing,english=english,syntax=syntax,malformed=malformed,nativeManualStructure=guide_structural or canonical,structuralCoverageComplete=not any((missing,english,syntax,malformed)) and (canonical or guide_structural)))
    return dict(distributionAuthorized=False,requiredMessages=len(requirements['requiredKeys']),authoredLanguages=len(result),structurallyCovered=sum(row['structuralCoverageComplete'] for row in result),languages=result)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo',type=Path,default=Path(__file__).resolve().parents[2])
    parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args();report=inspect(args.repo)
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({key:value for key,value in report.items() if key!='languages'},ensure_ascii=False))
    for row in report['languages']:
        if not row['missing']:
            print(row['code'], 'English:',len(row['english']),'syntax:',len(row['syntax']),'native manual:',row['nativeManualStructure'])
