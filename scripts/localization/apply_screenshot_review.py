"""Apply authored screenshot fixes without inventing translations for missing text."""
import json
from pathlib import Path

PACKS = Path(__file__).resolve().parents[2] / 'Localization'
CODES = ['uk-UA', 'ta-IN', 'mn-MN']

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def save(path, data): path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

def apply():
    ui = read(PACKS / 'reviewed-ui-keys.json')
    rows = {}
    for line in (PACKS / 'reviewed-screenshot-ui.tsv').read_text(encoding='utf-8').splitlines():
        if not line or line.startswith('#'): continue
        key, *values = line.split('|')
        assert len(values) == len(CODES), key
        if key.startswith('@ui'): key = ui[int(key[3:])]
        assert key not in rows, key
        rows[key] = [v.replace('\\n', '\n') for v in values]
    for index, code in enumerate(CODES):
        pack = read(PACKS / (code+'.json'))
        pack.update({key: values[index] for key, values in rows.items()})
        save(PACKS / (code+'.json'), pack)
    english = read(PACKS / 'en-US.json')
    english['HUD SETTINGS'] = 'HUD SETTINGS'
    save(PACKS / 'en-US.json', english)
    for locale in read(PACKS / 'reviewed-language-catalog.json')['languages']:
        code = locale['code']
        if code == 'en-US': continue
        pack = read(PACKS / (code+'.json'))
        pack.setdefault('HUD SETTINGS', 'HUD · '+pack['Settings'])
        # The product name and version are literal values; only the caption changes.
        for key, value in [('Endfield Charge Plus Settings', 'Endfield Charge Plus · '+pack['Settings']), ('{0} v{1} Settings', '{0} v{1} · '+pack['Settings'])]:
            if key not in pack or pack[key] == key: pack[key] = value
        save(PACKS / (code+'.json'), pack)
    core = read(PACKS / 'reviewed-core.json')
    core['messages']['HUD SETTINGS'] = [read(PACKS/(code+'.json'))['HUD SETTINGS'] for code in core['order']]
    save(PACKS / 'reviewed-core.json', core)
    print('Applied', len(rows), 'authored messages to each screenshot locale; localized the shared header for the whole catalog.')

if __name__ == '__main__': apply()
