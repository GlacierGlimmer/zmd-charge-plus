"""Build Android guides from authored instructions and platform-valid code examples.

Desktop prose is never converted by substituting operating-system names. A
missing native source stays missing in the completion audit instead of becoming
an English guide with a translated heading.
"""
import argparse,json,re
from pathlib import Path

from completion import CHAPTERS, platform_manual_errors

def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def export(source:Path,workspace:Path):
    packs=workspace/'zmd-charge-plus/Localization'
    dest=workspace/'zmd-charge-plus-for-android/core/src/main/resources/localization'
    assert workspace.is_absolute() and dest.is_dir() and (source/'manual.full.en-US.json').is_file()
    catalog=read(packs/'reviewed-language-catalog.json')['languages']
    canonical=read(source/'manual.full.en-US.json')
    examples={chapter['id']:[block for block in chapter['blocks'] if block.get('code')] for chapter in canonical}
    native_source=workspace/'zmd-charge-plus/Settings/NativeManualContent.cs'
    function_reference=re.search(r'private const string FunctionReference="""\r?\n(.*?)\r?\n""";',native_source.read_text(encoding='utf-8-sig'),re.S)
    assert function_reference,'Missing actual function/format reference'
    registry=workspace/'zmd-charge-plus-for-android/core/src/main/kotlin/com/glacierglimmer/endfieldchargeplus/core/metrics/VariableRegistry.kt'
    known_keys=set(re.findall(r'^\s*([a-z_][a-z0-9_.]+)\|',registry.read_text(encoding='utf-8-sig'),re.M))
    def android_example(code):
        # The examples retain the syntax; choose genuine mobile fields where a
        # desktop-only media session or battery-time alias has no Android reader.
        code=code.replace('{media.title|sub:0:24}','{time.current|sub:0:8}')
        code=code.replace('{battery.time_remaining_seconds|duration}','{time.target.remaining_seconds|duration}')
        referenced=set(re.findall(r'\b(?:cpu|gpu|memory|battery|time|network|frame|probe|ping|deepseek)\.[A-Za-z0-9_.]+',code))
        assert referenced<=known_keys,('Android manual example uses an unsupported variable',referenced-known_keys)
        return code
    targets=dict(start='home',display='display',profiles='content',variables='variables',hardware='hardware',sources='sources',cycle='content',summons='home',triggers='home',special='content',media='variables',instances='advanced',maintenance='advanced',troubleshooting='home')
    completed=[];missing=[]
    for locale in catalog:
        code=locale['code']
        target=dest/('manual.full.'+code+'.json')
        authored=packs/('manual.platform.'+code+'.json')
        errors=platform_manual_errors(authored,'android',packs/'manual.platform.en-US.json',code.startswith('en-'))
        if errors:
            # Remove the generated stale guide, never an authored file.
            target.unlink(missing_ok=True);missing.append(code);continue
        guide=read(authored);labels=guide['_labels'];doc=[]
        for kind in CHAPTERS:
            text=guide['android'][kind]
            blocks=[dict(id='guide',title=labels[0],body=text[2],kind='steps',code='',target=targets[kind]),
                    dict(id='details',title=labels[1],body=text[3],kind='text',code='',target='')]
            # Expressions and the HTTP JSON schema keep their executable syntax.
            if kind in {'profiles','variables','sources'}:
                for index,example in enumerate(examples[kind],1):
                    blocks.append(dict(id='example-'+str(index),title=labels[2]+' '+str(index),body=labels[3],kind='text',code=android_example(example['code']),target=''))
            if kind=='variables':
                blocks.append(dict(id='function-reference',title=labels[4],body=labels[3],kind='text',code=function_reference.group(1),target=''))
            doc.append(dict(id=kind,title=text[0],summary=text[1],blocks=blocks))
        write(target,doc)
        write(dest/authored.name,guide)
        completed.append(code)
    print(json.dumps(dict(androidGuidesWritten=completed,missingNativeAndroidGuides=missing),ensure_ascii=False))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('source',type=Path);parser.add_argument('workspace',type=Path);a=parser.parse_args();export(a.source,a.workspace)
