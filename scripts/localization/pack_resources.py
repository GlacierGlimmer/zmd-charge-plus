"""Build shipped packs from reviewed text only. Does not call a translator."""
import argparse
import ctypes
import json
import pathlib
import re
from variable_labels import labels as variable_labels

ROOT = pathlib.Path(__file__).resolve().parents[2]
PACKS = ROOT / "Localization"

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def traditional(text):
    replacements = {"保存并应用":"儲存並套用", "应用到":"套用至", "数据源":"資料來源", "默认":"預設", "变量":"變數", "硬件":"硬體", "软件":"軟體", "文件夹":"資料夾", "鼠标":"滑鼠", "内存":"記憶體", "显卡":"顯示卡", "驱动":"驅動程式", "网络":"網路", "窗口":"視窗", "配置":"設定", "数据":"資料", "信息":"資訊", "保存":"儲存", "汇总":"彙總", "当前":"目前", "链接":"連結", "禁用":"停用", "粘贴":"貼上"}
    for original, translated in replacements.items():
        text = text.replace(original, translated)
    mapper = ctypes.windll.kernel32.LCMapStringEx
    mapper.argtypes = [ctypes.c_wchar_p, ctypes.c_ulong, ctypes.c_wchar_p, ctypes.c_int, ctypes.c_wchar_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_longlong]
    size = mapper("zh-TW", 0x04000000, text, len(text), None, 0, None, None, 0)
    if not size:
        raise RuntimeError("Traditional Chinese conversion failed")
    buffer = ctypes.create_unicode_buffer(size + 1)
    if not mapper("zh-TW", 0x04000000, text, len(text), buffer, size, None, None, 0):
        raise RuntimeError("Traditional Chinese conversion failed")
    return buffer.value

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=pathlib.Path, required=True)
args = parser.parse_args()
source = read(args.source)
reviewed = read(PACKS / "reviewed-core.json")
for line in (PACKS / "reviewed-ui.txt").read_text(encoding="utf-8").splitlines():
    if not line.strip():
        continue
    key, *values = line.split("|")
    assert len(values) == len(reviewed["order"]), (key, len(values))
    reviewed["messages"][key] = values

ui_keys = read(PACKS / "reviewed-ui-keys.json")
bo_extra = {}
for line in (PACKS / "reviewed-status.txt").read_text(encoding="utf-8").splitlines():
    if line.strip():
        key, *values = re.split(r"\|(?![^{}]*\})", line)
        assert len(values) == len(reviewed["order"]) + 1, (key, len(values))
        reviewed["messages"][key] = values[:-1]
        bo_extra[key] = values[-1]
# New UI preferences are authored alongside the original reviewed tables.
for line in (PACKS / "reviewed-appearance.tsv").read_text(encoding="utf-8").splitlines():
    if line and not line.startswith("#"):
        row=line.split("|")
        if row[0]=="en-US":appearance_keys=row[1:]
for line in (PACKS / "reviewed-appearance.tsv").read_text(encoding="utf-8").splitlines():
    if not line or line.startswith("#"):continue
    row=line.split("|");code=row[0]
    for key,value in zip(appearance_keys,row[1:]):
        if code=="zh-CN":source[key]=value
        elif code=="bo-CN":bo_extra[key]=value
        elif code in reviewed["order"]:
            reviewed["messages"].setdefault(key,[""]*len(reviewed["order"]))[reviewed["order"].index(code)]=value

helpers = {}
for line in (PACKS / "reviewed-ui-complete.txt").read_text(encoding="utf-8").splitlines():
    if not line.strip():
        continue
    key, *values = line.split("|")
    assert len(values) == len(reviewed["order"]), (key, len(values))
    if key.startswith("@"):
        helpers[key] = values
    else:
        reviewed["messages"][ui_keys[int(key)]] = values

def finish_ui(pack, helper, manual):
    def u(index):
        return pack[ui_keys[index]]
    def put(index, value):
        pack[ui_keys[index]] = value
    put(2, "GPU (1): {0}")
    put(17, u(16) + "\n" + manual["special"][2].splitlines()[0])
    put(36, u(37).replace("{0}", "0.1.2"))
    put(39, pack["Custom Profile"] + " {0}")
    put(44, "DeepSeek API")
    put(46, pack["Display"] + " {0} · {1}×{2}{3}")
    put(50, u(38) + ": ")
    put(52, pack["Export"] + " Endfield Charge Plus")
    put(65, pack["Import"] + " Endfield Charge Plus")
    put(70, "KB/s / MB/s (" + helper["@auto"] + ")")
    put(71, "max(" + u(47) + ", " + u(141) + ")")
    for index, label in ((73,"@checkfailed"),(74,"@unchecked"),(75,"@timeout")):
        put(index, helper["@latest"] + ": " + helper[label])
    put(76, helper["@latest"] + ": " + pack["Unavailable"])
    put(77, helper["@latest"] + ": v{0}")
    for alias, original in ((97,95),(104,103),(107,60),(110,111),(133,9),(134,3),(184,144)):
        put(alias, u(original))
    put(112, pack["Target window"] + "…")
    for index, suffix in ((121,helper["@checkfailed"]),(122,"GitHub…"),(126,helper["@unchecked"]),(130,helper["@timeout"])):
        put(index, u(120) + ": " + suffix)
    put(131, pack["System"] + " · " + u(120))
    put(132, manual["variables"][0] + ": {variable|format}  {= expression | format}\n" + manual["_labels"][4] + ": + - * / % ^  > >= < <= == !=  && / || / !  ?:  ??  if() min/max/avg/sum/clamp/round\n" + pack["Progress Value"] + ": = expression\n" + manual["_labels"][2] + ": {= if(memory.usage >= 80, 'High', 'OK')}")
    put(137, helper["@total"] + " (" + u(47) + " + " + u(141) + ")")
    put(138, pack["Search chapters or keys"] + " / " + pack["Clear search"])
    put(151, "X / px")
    put(152, "Y / px")
    put(153, "cpu.usage " + helper["@or"] + " = (cpu.usage + gpu.usage) / 2")
    put(154, manual["_labels"][2] + ": 1.1.1.1, 2606:4700:4700::1111, example.com")
    put(156, "{0} (" + pack["Custom Profile"] + ")")
    put(157, "{0} GPU")
    for index in range(170,178):
        put(index, pack[ui_keys[index].title()])
    for name in ("Battery", "Memory", "System Disk", "Network", "Time", "World Clock", "Packet Probe", "Display FPS", "Window FPS"):
        tag = "NETWORK PROBE" if name == "Packet Probe" else name.upper()
        pack["/// " + tag] = "/// " + pack[name]
    for name in ("Display FPS", "Window FPS"):
        pack[name + " (Run as administrator required)"] = pack[name] + " (" + pack["Run as administrator"] + ")"
    for key in ("Check drivers and sensors under Settings → Data Management → Hardware Data.", "Check this sensor or the AIDA64 sharing settings under Settings → Data Management → Hardware Data."):
        pack[key] = pack["Settings"] + " → " + pack["Data Management"] + " → " + pack["Hardware Data"] + "\n" + u(80)

for index, code in enumerate(reviewed["order"]):
    current = {key:values[index] for key,values in reviewed["messages"].items()}
    manual_path = PACKS / ("manual." + code + ".json")
    # Traditional Chinese uses the Chinese source for this short instruction.
    manual = read(manual_path) if manual_path.exists() else {"special":["","","世界時鐘可調整時區與每日目標。"], "variables":["變數與運算式"], "_labels":["步驟","說明","範例","","函數速查"]}
    finish_ui(current, {key:values[index] for key,values in helpers.items()}, manual)
    for key in ui_keys:
        assert key in current, (code, "unreviewed UI", key)
        if key not in reviewed["messages"]:
            reviewed["messages"][key] = [""] * len(reviewed["order"])
        reviewed["messages"][key][index] = current[key]
    for key,value in current.items():
        if key not in reviewed["messages"]:
            reviewed["messages"][key] = [""] * len(reviewed["order"])
        reviewed["messages"][key][index] = value

aliases = {"Off":"Disabled", "Preview":"Preview HUD", "Check for updates":"Check Updates", "Network Probe":"Packet Probe", "HUD Content & Data":"HUD Content"}
for alias, key in aliases.items():
    if alias in source:
        reviewed["messages"][alias] = reviewed["messages"][key]

reviewed["messages"]["{0} v{1} Settings"] = ["{0} v{1} " + text for text in reviewed["messages"]["Settings"]]
reviewed["messages"]["Endfield Charge Plus Settings"] = ["Endfield Charge Plus " + text for text in reviewed["messages"]["Settings"]]
for position in ("Top Left", "Top Center", "Top Right", "Center Left", "Center", "Center Right", "Bottom Left", "Bottom Center", "Bottom Right"):
    label = reviewed["messages"][position]
    for area in ("work area", "above taskbar / Dock"):
        key = position + " (" + area + ")"
        if key in source:
            reviewed["messages"][key] = [p + " (" + a + ")" for p,a in zip(label,reviewed["messages"][area])]
    key = "Screen " + position
    if key in source:
        reviewed["messages"][key] = [s + ": " + p for s,p in zip(reviewed["messages"]["Display"],label)]

# Keep the complete immutable catalog. Unreviewed wording deliberately falls
# back to English rather than publishing an unreliable translation draft.
origins_path = args.source.with_name("catalog.origins.json")
if origins_path.exists():
    missing = []
    for key, origins in read(origins_path).items():
        ui_origin = any((name.replace("\\", "/").startswith(("Settings/", "Views/", "Interop/")) and not name.endswith("UserManualContent.cs")) or name.replace("\\", "/") in ("Customization/HudCustomizerView.axaml.cs", "Customization/HudCustomizerView.Triggers.cs", "Customization/HudCustomizerView.Frames.cs", "LocalizationManager.cs", "ToggleSwitch") for name in origins)
        if ui_origin and key not in reviewed["messages"]:
            missing.append(key)
    assert not missing, ("Settings UI requires reviewed translations", missing)
save(PACKS / "en-US.json", {key:key for key in source})
save(PACKS / "zh-CN.json", source)
for code in reviewed["order"]:
    pack = {key:traditional(value) if code == "zh-TW" else key for key,value in source.items()}
    index = reviewed["order"].index(code)
    for key, values in reviewed["messages"].items():
        if key in source:
            pack[key] = values[index]
    save(PACKS / (code + ".json"), pack)
save(PACKS / "reviewed-core.json", reviewed)
bo_path = PACKS / "reviewed-bo-CN.json"
bo = None
if bo_path.exists():
    bo = read(bo_path)
    bo.update(bo_extra)
    for line in (PACKS / "reviewed-bo-ui.txt").read_text(encoding="utf-8").splitlines():
        if line.strip():
            index, value = line.split("|",1)
            bo[ui_keys[int(index)]] = value
    finish_ui(bo, bo, read(PACKS / "manual.bo-CN.json"))
    for alias,key in aliases.items():
        bo[alias] = bo[key]
    bo["{0} v{1} Settings"] = "{0} v{1} " + bo["Settings"]
    bo["Endfield Charge Plus Settings"] = "Endfield Charge Plus " + bo["Settings"]
    for position in ("Top Left", "Top Center", "Top Right", "Center Left", "Center", "Center Right", "Bottom Left", "Bottom Center", "Bottom Right"):
        for area in ("work area", "above taskbar / Dock"):
            bo[position + " (" + area + ")"] = bo[position] + " (" + bo[area] + ")"
        bo["Screen " + position] = bo["Display"] + ": " + bo[position]
    assert all(key in bo for key in reviewed["messages"]), ("Tibetan messages missing", [key for key in reviewed["messages"] if key not in bo])
    save(PACKS / "bo-CN.json", {key:bo.get(key,key) for key in source})
metadata_path = args.source.with_name("catalog.variables.json")
if metadata_path.exists():
    metadata = read(metadata_path)
    term_rows = {}
    for line in (PACKS / "reviewed-variable-terms.txt").read_text(encoding="utf-8").splitlines():
        if line.strip():
            key,*values=line.split("|")
            assert len(values)==len(reviewed["order"])+1, (key,len(values))
            term_rows[key]=values
    for index,code in enumerate(reviewed["order"] + ["bo-CN"]):
        curated = {key:values[index] for key,values in reviewed["messages"].items()} if code!="bo-CN" else bo.copy()
        if code=="zh-TW":
            # Retain the richer original Chinese labels wherever available.
            original=read(PACKS/(code+".json"))
            for key,value in original.items():
                if key not in curated and re.search(r"[\u3400-\u9fff]",value):
                    curated[key]=value
        terms = {key:values[index] for key,values in term_rows.items()}
        additions=variable_labels(curated,metadata,terms)
        def t(key):
            return terms.get(key,curated.get(key,key))
        additions.update({"Media":t("Media"),"ECP App":"ECP · "+t("App"),"Ping (Compatibility)":"Ping ("+t("Compatibility")+")","USB / Devices":"USB / "+t("Device")})
        aliases = {"Left info":"Left Main Info", "Left main info":"Left Main Info", "Right status":"Right Status", "Primary value":"Primary", "Secondary value":"Secondary / Note", "Custom":"Custom Data", "HUD content / event triggers":"Trigger rules"}
        for field in {v["RecommendedUse"] for v in metadata.values()}:
            if field in aliases:
                additions[field]=t(aliases[field])
                continue
            parts=[]
            for part in field.split(" / "):
                if part=="Status calculation":parts.append(t("Status")+" · "+t("Compute"))
                else:parts.append(t(aliases.get(part,part)))
            additions[field]=" / ".join(parts)
        pack=read(PACKS/(code+".json"))
        assert all(key in source for key in additions), (code,"metric not in source catalog",set(additions)-source.keys())
        pack.update(additions)
        save(PACKS/(code+".json"),pack)
    save(PACKS/"reviewed-metrics.json", {"names":sorted({v["Name"] for v in metadata.values()}),"uses":sorted({v["RecommendedUse"] for v in metadata.values()}),"technicalNames":["CPU","GPU","DeepSeek API","PID","Iso"],"terms":len(term_rows)})
print(f"Built offline language packs: {len(source)} catalog entries, {len(reviewed['messages'])} reviewed messages.")
