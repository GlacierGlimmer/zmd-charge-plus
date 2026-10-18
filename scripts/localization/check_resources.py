"""Check shipped language packs locally; no network or inference dependencies."""
import argparse
import collections
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
PACKS = ROOT / "Localization"
LANGUAGES = "zh-CN zh-TW en-US ja-JP ko-KR de-DE fr-FR es-ES pt-BR ru-RU ar-SA hi-IN bn-BD ur-PK id-ID vi-VN th-TH tr-TR it-IT fa-IR bo-CN".split()
CHAPTERS = "start display profiles variables hardware sources cycle summons triggers special media instances maintenance troubleshooting".split()
TOKENS = re.compile(r"\$\{[^{}\n]*\}|\{[^{}\n]*\}|\b[a-z_][a-z0-9_-]*(?:\.[a-z0-9_<>-]+)+")

def read(path):
    def unique(items):
        result = {}
        for key, value in items:
            assert key not in result, f"Duplicate key in {path.name}: {key}"
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique)

parser=argparse.ArgumentParser()
parser.add_argument('--complete',action='store_true',help='Fail if any catalog locale still has English fallback messages')
args=parser.parse_args()
catalog=read(PACKS/'reviewed-language-catalog.json')['languages']
assert len(catalog)==146 and sum(bool(l.get('minecraftCode')) for l in catalog)==143
assert len({l['code'] for l in catalog})==146
english = read(PACKS / "en-US.json")
reviewed = read(PACKS / "reviewed-core.json")
ui_keys = read(PACKS / "reviewed-ui-keys.json")
metrics = read(PACKS / "reviewed-metrics.json")
term_keys={line.split("|",1)[0] for line in (PACKS/"reviewed-variable-terms.txt").read_text(encoding="utf-8").splitlines() if line.strip()}
assert all(key in english and key in reviewed["messages"] for key in ui_keys), "UI collection is incomplete"
def tokens(text):
    # e.g. is prose, not a path or a variable identifier.
    return collections.Counter(t for t in TOKENS.findall(text) if t != "e.g")
assert all(key == value for key, value in english.items())
assert len(english) > 1500
for code in LANGUAGES:
    pack = read(PACKS / (code + ".json"))
    assert pack.keys() == english.keys(), f"Missing or stale messages: {code}"
    for key, value in pack.items():
        assert isinstance(value, str) and value.strip(), (code, key, "empty")
        assert not re.search(r"\ufffd|<unk>|7845\d{4}", value), (code, key, "broken text")
        if code not in ("zh-CN", "zh-TW"):
            assert tokens(key) == tokens(value), (code, key, "changed syntax")
    if code in reviewed["order"]:
        index = reviewed["order"].index(code)
        for key, values in reviewed["messages"].items():
            assert len(values) == len(reviewed["order"]), key
            if key in english:
                assert pack[key] == values[index], (code, key, "reviewed wording not applied")
    if code not in ("zh-CN", "en-US"):
        for key in ui_keys:
            assert key in reviewed["messages"] and pack[key], (code, key, "unreviewed UI")
            if len(key.split()) >= 6 and not key.startswith("cpu.usage"):
                assert pack[key] != key, (code,key,"English UI fallback")
        for key in ("Multiple HUD instances", "Enable multiple HUD instances", "No shortcut configured.", "On", "Top center", "Immediate"):
            assert pack[key] != key, (code,key,"screenshot regression")
        for name in metrics["names"]:
            if len(name.split())>=2 and name not in metrics["technicalNames"]:
                assert pack[name]!=name, (code,name,"English metric label")
        for name in metrics["uses"]:
            if name not in reviewed["messages"] and name not in term_keys:
                assert pack[name]!=name, (code,name,"English usage guidance")
    if code not in ("zh-CN", "zh-TW", "en-US"):
        manual = read(PACKS / ("manual." + code + ".json"))
        assert set(manual) == set(CHAPTERS + ["_labels"]), code
        assert len(manual["_labels"]) == 5, code
        for chapter in CHAPTERS:
            assert len(manual[chapter]) == 4 and all(manual[chapter]), (code, chapter)
            assert len(manual[chapter][2].splitlines()) >= 3, (code, chapter, "missing steps")
            assert not re.search(r"\ufffd|<unk>|7845\d{4}", "".join(manual[chapter])), (code, chapter)
    print(f"PASS {code}: {len(pack)} messages, 14 manual chapters")
print("PASS original 21 UI packs and native guides; catalog checks follow separately")
partial=[]
for locale in catalog:
    code=locale['code'];pack=read(PACKS/(code+'.json'))
    for key in ['Settings','Display & Position','HUD Content','Data Management','Experimental Features','User Manual','About','Done','Global font size and UI scale']:
        assert key in pack and pack[key].strip(),(code,key,'missing core navigation')
    for key,value in pack.items():
        assert isinstance(value,str) and value.strip(),(code,key,'empty')
        if code not in ('zh-CN','zh-TW','zh-HK'):assert tokens(key)==tokens(value),(code,key,'changed syntax')
    missing=[key for key in english if key not in pack]
    if missing:partial.append((code,len(missing)))
print('PASS catalog146, bundled146 and immutable syntax; partial message sets:',len(partial))
if args.complete:
    assert not partial,('Unfinished translation sets',partial)
