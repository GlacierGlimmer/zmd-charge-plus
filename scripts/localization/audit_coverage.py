"""Audit every bundled locale and platform, including actual English fallback.

Presence of a key is not evidence of translation. This report does not fill gaps
or mark English with a localized prefix as translated. Technical identifiers,
units, product names and code samples remain immutable.
"""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENGLISH_CODES = {'en-US', 'en-GB', 'en-AU', 'en-CA', 'en-NZ', 'en-x-pirate', 'en-x-upside-down', 'en-x-anglish', 'en-x-shakespeare', 'en-x-lolcat'}
TECHNICAL = set('Endfield Charge Plus HUD UI CPU GPU RAM VRAM API DeepSeek AIDA64 PawnIO Windows Linux macOS Android JSON HTTP HTTPS GET POST WMI SMBIOS BIOS UEFI AC DC PCI PCIe USB NVIDIA AMD Intel OpenGL Vulkan DirectX FPS UTC ICMP TCP UDP IPv4 IPv6 DNS TCPv4 TCPv6 OS SDK GB MB KB TB GiB MiB KiB TiB B s ms Hz kHz MHz GHz W mW V mV C F dBm Mbps px pt DPI RGB EN ZH JSONPath GitHub UTF USB SSD HDD SATA NVMe SPD XMP EXPO ACPI TPM UAC KB/s MB/s OFL .NET'.casefold().split())
SLOTS = re.compile(r'\$\{[^{}]*\}|\{[^{}]*\}')
IDENTIFIERS = re.compile(r'\b[a-z_][a-z0-9_-]*(?:\.[a-z0-9_<>-]+)+|https?://\S+')

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))

def prose(key):
    if key.lstrip().startswith(('{', '=', 'avares://')): return False
    text = IDENTIFIERS.sub('', SLOTS.sub('', key))
    words = re.findall(r'[A-Za-z]+(?:-[A-Za-z]+)*', text)
    return bool(words) and any(word.casefold() not in TECHNICAL for word in words)

def unchanged_english(key, value):
    if not prose(key): return False
    if value.strip() == key.strip(): return True
    # Prefixing a sentence with native text must not turn it into a passing translation.
    return len(re.findall(r'[A-Za-z]+', key)) >= 6 and key.strip() in value

def audit(workspace, master):
    packs = workspace/'zmd-charge-plus/Localization'
    source = read(packs/'en-US.json')
    required = set(source)
    if master: required.update(read(master))
    origins_path=master.with_name('catalog.origins.json') if master else None
    origins=read(origins_path) if origins_path and origins_path.is_file() else {}
    catalog = read(packs/'reviewed-language-catalog.json')['languages']
    platforms = {
        'Windows': packs,
        'Linux': workspace/'zmd-charge-plus-for-linux/Localization',
        'macOS': workspace/'zmd-charge-plus-for-macos/Localization',
        'Android': workspace/'zmd-charge-plus-for-android/core/src/main/resources/localization',
    }
    result = []
    for platform, directory in platforms.items():
        for locale in catalog:
            code = locale['code']; path = directory/(code+'.json')
            pack = read(path) if path.is_file() else {}
            english = code in ENGLISH_CODES
            manual = directory/('manual.full.'+code+'.json' if platform=='Android' else 'manual.'+code+'.json')
            # Canonical English/Chinese manuals are C# data on desktop. Android
            # requires a real exported guide for the selected code.
            has_manual = manual.is_file() or platform!='Android' and (english or code in {'zh-CN','zh-TW','zh-HK'})
            def used(key):
                locations=origins.get(key,[])
                if locations and not any(p.startswith(platform.lower()+'/') for p in locations): return False
                # Native guides replace the canonical desktop prose at runtime;
                # report the active guide instead of counting unused English data.
                return not (has_manual and locations and all(p.replace('\\','/').endswith('/Settings/UserManualContent.cs') for p in locations))
            active={key for key in required if used(key)}
            missing = sorted(active-pack.keys()) if not english else []
            fallback = sorted(key for key,value in pack.items() if key in active and not english and unchanged_english(key,value))
            manual_english=[]
            if platform=='Android' and manual.is_file() and not english:
                canonical=read(directory/'manual.full.en-US.json')
                sentences={value for chapter in canonical for value in [chapter['title'],chapter['summary']]+[s for b in chapter['blocks'] for s in [b['title'],b['body']]] if prose(value)}
                for chapter in read(manual):
                    for value in [chapter['title'],chapter['summary']]+[s for b in chapter['blocks'] for s in [b['title'],b['body']]]:
                        if value in sentences: manual_english.append(value)
            result.append(dict(platform=platform,code=code,requiredMessages=len(active),bundledMessages=len(pack),missingKeys=missing,englishProse=fallback,nativeManual=has_manual,englishManualProse=sorted(set(manual_english)),complete=not missing and not fallback and has_manual and not manual_english))
    return dict(schema=1,localeCount=len(catalog),platformCount=len(platforms),requiredMessages=len(required),completeLanguagePlatformPairs=sum(row['complete'] for row in result),incompleteLanguagePlatformPairs=sum(not row['complete'] for row in result),languages=result)

if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--workspace',type=Path,default=ROOT.parent)
    parser.add_argument('--master',type=Path)
    parser.add_argument('--report',type=Path,required=True)
    parser.add_argument('--fail-on-gaps',action='store_true')
    args=parser.parse_args()
    report=audit(args.workspace,args.master)
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({key:value for key,value in report.items() if key!='languages'},ensure_ascii=False))
    if args.fail_on_gaps and report['incompleteLanguagePlatformPairs']: raise SystemExit(1)
