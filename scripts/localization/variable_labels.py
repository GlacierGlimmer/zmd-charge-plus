"""Compose short metric labels from reviewed terms, keeping technical tokens intact."""
import re

ALIASES = {"avg":"Average", "min":"Minimum", "max":"Maximum", "cores":"Core", "processors":"Processor", "context switches":"Context Switch", "switches":"Switch", "interrupts":"Interrupt", "instructions":"Instruction", "names":"Name", "off peak":"Off-peak", "en":"EN", "zh":"ZH", "dpi":"DPI", "addresses":"Address", "connections":"Connection", "containers":"Container", "errors":"Error", "gateways":"Gateway", "hours":"Hour", "images":"Image", "lanes":"Lane", "milliseconds":"Millisecond", "packets":"Packet", "seconds":"Second", "servers":"Server", "threats":"Threat", "types":"Type", "gen":"Generation", "nyc":"New York", "dbm":"dBm", "watts":"W"}
TECHNICAL = "AC AIDA64 API AMD Adrenalin BIOS BitLocker CPU C D DNS DPC Defender Docker Git Golang GPU Hyper-V IPv4 IPv6 IDE ISO Java KB L2 L3 LLM ms MHz mWh Wh NVIDIA Node OS PCIe PID px Python Rust SmartScreen TCP TPM TTL UAC UDP UTC Unix VRAM VSCode VPN WSL Wi-Fi X Y 1m 5m 15m 3D B/s Mbps GHz ID mV V W °C I/O GB RAM EN ZH DPI dBm".split()
TECHNICAL += ["NVIDIA SMI", "AMD Adrenalin", "Hyper V", "Nvidia Smi", "12h", "SMI", "SMART", "TRIM", "Windows"]

def labels(pack, metadata, terms):
    lexicon = {key.casefold(): value for key,value in pack.items() if len(key)<65 and "{" not in key and not key.startswith("///") and value}
    lexicon.update({key.casefold():value for key,value in terms.items()})
    lexicon.update({key.casefold():key for key in TECHNICAL})
    lexicon.update({"nvidia smi":"NVIDIA SMI", "amd adrenalin":"AMD Adrenalin", "hyper v":"Hyper-V", "vscode":"VS Code"})
    for alias, original in ALIASES.items():
        # Aliases may contain two reviewed terms and one fixed unit.
        lexicon[alias] = lexicon.get(original.casefold(), original)
    # These words add no meaning to a compact boolean metric label.
    lexicon.update({key:"" for key in ("is", "as", "uses", "of", "to", "per", "vs")})
    lexicon["has"] = pack["Available"]
    lexicon["saver on"] = pack["Battery"] + " · " + pack["Save"]
    lexicon["rate watts"] = terms["Power"] + " · W"
    phrases=sorted(lexicon,key=lambda s:(-len(s),s))
    missing=set()
    result={}
    for name in sorted({value["Name"] for value in metadata.values()}):
        if name in pack:
            continue
        remaining=name.casefold()
        chunks=[]
        while remaining:
            found=next((key for key in phrases if remaining==key or remaining.startswith(key+" ")),None)
            if found is not None:
                if lexicon[found]:chunks.append(lexicon[found])
                remaining=remaining[len(found):].strip()
            else:
                word,_,remaining=remaining.partition(" ")
                if word!="-":missing.add(word)
        if not missing:
            result[name]=" · ".join(chunks)
    if missing:
        raise ValueError("Metric terms need review: "+", ".join(sorted(missing)))
    return result
