"""Adapt desktop guide exports to Android capabilities; never advertise desktop-only APIs."""
import argparse,copy,json
from pathlib import Path

def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def export(source:Path,workspace:Path):
    packs=workspace/'zmd-charge-plus/Localization'
    dest=workspace/'zmd-charge-plus-for-android/core/src/main/resources/localization'
    assert workspace.is_absolute() and dest.is_dir() and (source/'manual.full.en-US.json').is_file()
    catalog=read(packs/'reviewed-language-catalog.json')['languages']
    english=next(line.split('|')[2] for line in (packs/'reviewed-platform-manual.tsv').read_text(encoding='utf-8').splitlines() if line.startswith('en-US|'))
    hint='Shows readings actually provided by the device and system. Unavailable items show -- and their reason.'
    for locale in catalog:
        code=locale['code'];pack=read(packs/(code+'.json'))
        target=dest/('manual.full.'+code+'.json')
        # An untranslated guide is served by the explicit English fallback. Do
        # not inflate native-guide coverage by saving 100 identical English copies.
        if len(pack)<100 and code!='en-US':
            target.unlink(missing_ok=True);continue
        doc=read(source/target.name)
        def t(text):return pack.get(text,text)
        def block(body,title='Details',target=''):
            return dict(id='android-guide',title=t(title),body=body,kind='text',code='',target=target)
        android=t(english)
        for chapter in doc:
            kind=chapter['id']
            # Raw code examples and immutable variable syntax stay exactly as exported.
            if kind in ('summons','triggers','instances','media'):
                chapter['summary']=t('Unsupported')+' · Android'
                chapter['blocks']=[block(chapter['title']+' · '+t('Unsupported')+'\n\n'+android,target='home')]
            elif kind=='start':
                chapter['summary']=t('Home')+' → HUD'
                chapter['blocks']=[block(android,target='home'),block(t('HUD Content')+' → '+t('Built-in Profiles')+' → '+t('Save'),target='content')]
            elif kind=='display':
                chapter['blocks']=[block(t('Display & Position')+' → '+t('Always Visible')+' / '+t('Display mode')+' / '+t('Position')+' / '+t('Opacity'),target='display'),
                    block(t('Applies to all settings and every HUD. Individual HUD size is multiplied by global UI scale.'),'Global font size and UI scale',target='display'),block(android)]
            elif kind=='hardware':
                chapter['summary']=t('Hardware Data')
                chapter['blocks']=[block(t(hint)+'\n\n'+android,target='hardware'),
                    block(t('Data Management')+' → '+t('Hardware Data')+' → '+t('Copy template'),target='hardware')]
            elif kind=='sources':
                chapter['summary']=t('Data Source Configuration')
                chapter['blocks']=[b for b in chapter['blocks'] if b['id']!='aida64']
                for b in chapter['blocks']:
                    b['body']='\n'.join(line.replace('Windows','Android') for line in b['body'].splitlines() if 'AIDA64' not in line)
                chapter['blocks'].insert(0,block(android+'\nAndroid Keystore',target='sources'))
            elif kind=='special':
                chapter['blocks']=[block(t('World Clock')+' → UTC\n'+t('Network')+' → Mbps / MB/s\n'+t('Packet Probe')+' → ICMP / TCP / UDP',target='content'),block(t(hint)+'\n\n'+android)]
            elif kind=='maintenance':
                chapter['blocks']=[block(t('Experimental Features')+' → '+t('Export')+' / '+t('Import')+'\nAndroid Keystore',target='advanced'),block(android)]
            elif kind=='troubleshooting':
                chapter['blocks']=[block(t('Home')+' → '+t('Permissions')+'\n'+t('Display & Position')+' → '+t('Always Visible')+'\n'+t('Data Management')+' → '+t('Hardware Data')+'\n\n'+android,target='home')]
            else:
                for b in chapter['blocks']:
                    b['body']=b['body'].replace(t('Save & Apply'),t('Save'))
        write(target,doc)
    print('Android guides adapted to actual mobile controls and capabilities.')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('source',type=Path);parser.add_argument('workspace',type=Path);a=parser.parse_args();export(a.source,a.workspace)
