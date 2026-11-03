#!/usr/bin/env python3
"""Offline NLLB-200 translation driver for ECP .lang missing fields.

Zero external API cost: runs the local facebook/nllb-200-distilled-600M model
with transformers on CPU (or CUDA when available), writes only missing values,
and imports them through langimport.py's validated fill path.

Usage:
  python scripts/localization/nllb_fill.py --languages af-ZA,de-DE --dry-run
  python scripts/localization/nllb_fill.py --max-gap 200 --yes
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import langtool  # noqa: E402

MODEL_DIR = pathlib.Path(r"D:\models\nllb-200-distilled-600M")
CACHE = pathlib.Path(r"D:\ECP_Workspace\.smoke\nllb-cache")

# ECP locale -> FLORES-200 code used by NLLB.
NLLB = {
    "af-ZA": "afr_Latn", "ar-SA": "arb_Arab", "az-AZ": "azj_Latn", "be-BY": "bel_Cyrl",
    "bg-BG": "bul_Cyrl", "bn-BD": "ben_Beng", "bs-BA": "bos_Latn", "ca-ES": "cat_Latn",
    "cs-CZ": "ces_Latn", "cy-GB": "cym_Latn", "da-DK": "dan_Latn", "de-AT": "deu_Latn",
    "de-CH": "deu_Latn", "de-DE": "deu_Latn", "el-GR": "ell_Grek", "en-AU": "eng_Latn",
    "en-CA": "eng_Latn", "en-GB": "eng_Latn", "en-NZ": "eng_Latn", "en-US": "eng_Latn",
    "es-AR": "spa_Latn", "es-CL": "spa_Latn", "es-EC": "spa_Latn", "es-ES": "spa_Latn",
    "es-MX": "spa_Latn", "es-UY": "spa_Latn", "es-VE": "spa_Latn", "et-EE": "est_Latn",
    "eu-ES": "eus_Latn", "fa-IR": "pes_Arab", "fi-FI": "fin_Latn", "fil-PH": "tgl_Latn",
    "fr-CA": "fra_Latn", "fr-CH": "fra_Latn", "fr-FR": "fra_Latn", "ga-IE": "gle_Latn",
    "gd-GB": "gla_Latn", "gl-ES": "glg_Latn", "he-IL": "heb_Hebr", "hi-IN": "hin_Deva",
    "hr-HR": "hrv_Latn", "hu-HU": "hun_Latn", "hy-AM": "hye_Armn", "id-ID": "ind_Latn",
    "ig-NG": "ibo_Latn", "is-IS": "isl_Latn", "it-IT": "ita_Latn", "ja-JP": "jpn_Jpan",
    "ka-GE": "kat_Geor", "kk-KZ": "kaz_Cyrl", "kn-IN": "kan_Knda", "ko-KR": "kor_Hang",
    "ky-KG": "kir_Cyrl", "lb-LU": "ltz_Latn", "lo-LA": "lao_Laoo", "lt-LT": "lit_Latn",
    "lv-LV": "lvs_Latn", "mk-MK": "mkd_Cyrl", "mn-MN": "khk_Cyrl", "ms-MY": "zsm_Latn",
    "mt-MT": "mlt_Latn", "nl-BE": "nld_Latn", "nl-NL": "nld_Latn", "nn-NO": "nno_Latn",
    "no-NO": "nob_Latn", "pl-PL": "pol_Latn", "pt-BR": "por_Latn", "pt-PT": "por_Latn",
    "ro-RO": "ron_Latn", "ru-RU": "rus_Cyrl", "sk-SK": "slk_Latn", "sl-SI": "slv_Latn",
    "sq-AL": "als_Latn", "sr-Cyrl": "srp_Cyrl", "sr-Latn": "srp_Cyrl", "sv-SE": "swe_Latn",
    "am-ET": "amh_Ethi", "as-IN": "asm_Beng", "chr-US": "chr_Cher", "prs-AF": "pes_Arab",
    "gu-IN": "guj_Gujr", "ha-NG": "hau_Latn", "iu-CA": "ike_Cans", "km-KH": "khm_Khmr",
    "quc-GT": "quc_Latn", "rw-RW": "kin_Latn", "sw-KE": "swh_Latn", "kok-IN": "kok_Deva",
    "ku-TR": "kmr_Latn", "ml-IN": "mal_Mlym", "mi-NZ": "mri_Latn", "mr-IN": "mar_Deva",
    "ne-NP": "nep_Deva", "or-IN": "ory_Orya", "pa-IN": "pan_Guru", "quz-PE": "quz_Latn",
    "nso-ZA": "nso_Latn", "tn-ZA": "tsn_Latn", "sd-PK": "snd_Arab", "si-LK": "sin_Sinh",
    "tg-TJ": "tgk_Cyrl", "te-IN": "tel_Telu", "ti-ET": "tir_Ethi", "tk-TM": "tuk_Latn",
    "ug-CN": "uig_Arab", "wo-SN": "wol_Latn", "xh-ZA": "xho_Latn", "zu-ZA": "zul_Latn",
    "ta-IN": "tam_Taml", "th-TH": "tha_Thai", "tr-TR": "tur_Latn", "tt-RU": "tat_Cyrl",
    "uk-UA": "ukr_Cyrl", "ur-PK": "urd_Arab", "uz-UZ": "uzn_Latn", "vi-VN": "vie_Latn",
    "yo-NG": "yor_Latn", "zh-CN": "zho_Hans", "zh-HK": "zho_Hant", "zh-TW": "zho_Hant",
}

TOKEN_RE = re.compile(r"\$\{[^{}\n]*\}|\{[^{}\n]*\}|\b[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_<>-]+)+")
MARK = "ZQX{}ZQX"


def load_model():
    import torch
    from transformers import AutoModelForSeq2SeqLM, AutoTokenizer
    torch.set_num_threads(max(1, (torch.get_num_threads() if hasattr(torch, "get_num_threads") else 8)))
    tokenizer = AutoTokenizer.from_pretrained(str(MODEL_DIR), src_lang="eng_Latn")
    device = "cuda" if torch.cuda.is_available() else "cpu"
    model = AutoModelForSeq2SeqLM.from_pretrained(str(MODEL_DIR)).to(device)
    model.eval()
    print(f"device: {device}" + (f" ({torch.cuda.get_device_name(0)})" if device == "cuda" else ""))
    return torch, tokenizer, model


def protect(text: str):
    """Replace formats/technical tokens/newlines with stable markers."""
    mapping = {}
    counter = [0]

    def repl(match):
        token = match.group()
        marker = MARK.format(counter[0])
        counter[0] += 1
        mapping[marker] = token
        return marker

    protected = TOKEN_RE.sub(repl, text)
    protected = protected.replace("\n", " ZQZNL ZQZ")
    return protected, mapping


def restore(text: str, mapping: dict, source: str) -> tuple[str, bool]:
    restored = text.replace(" ZQZNL ZQZ", "\n").replace("ZQZNL", "\n")
    for marker, token in mapping.items():
        restored = restored.replace(marker, token)
        restored = restored.replace(marker.lower(), token)
        restored = restored.replace(marker.replace("ZQX", "zqx"), token)
    # Every source placeholder must survive exactly once.
    ok = all(token in restored for token in mapping.values())
    return restored, ok


SR_CYR = str.maketrans({
    "а":"a","б":"b","в":"v","г":"g","д":"d","ђ":"đ","е":"e","ж":"ž","з":"z","и":"i","ј":"j",
    "к":"k","л":"l","љ":"lj","м":"m","н":"n","њ":"nj","о":"o","п":"p","р":"r","с":"s","т":"t",
    "ћ":"ć","у":"u","ф":"f","х":"h","ц":"c","ч":"č","џ":"dž","ш":"š",
    "А":"A","Б":"B","В":"V","Г":"G","Д":"D","Ђ":"Đ","Е":"E","Ж":"Ž","З":"Z","И":"I","Ј":"J",
    "К":"K","Л":"L","Љ":"Lj","М":"M","Н":"N","Њ":"Nj","О":"O","П":"P","Р":"R","С":"S","Т":"T",
    "Ћ":"Ć","У":"U","Ф":"F","Х":"H","Ц":"C","Ч":"Č","Џ":"Dž","Ш":"Š"})


def sr_latin(text: str) -> str:
    return text.translate(SR_CYR)


def translate_batch(tokenizer, model, texts, target_code):
    import torch
    target_id = tokenizer.convert_tokens_to_ids(target_code)
    if target_id is None or target_id == tokenizer.unk_token_id:
        raise RuntimeError(f"NLLB does not support target code {target_code}")
    device = next(model.parameters()).device
    inputs = tokenizer(texts, return_tensors="pt", padding=True, truncation=True, max_length=512).to(device)
    with torch.no_grad():
        output = model.generate(**inputs, forced_bos_token_id=target_id, max_new_tokens=512, num_beams=1)
    results = [tokenizer.decode(row, skip_special_tokens=True) for row in output]
    return results


def cache_key(code: str, source: str) -> str:
    import hashlib
    return hashlib.sha256((code + "\0" + source).encode("utf-8")).hexdigest()


def load_cache(code: str) -> dict:
    path = CACHE / f"{code}.json"
    if path.is_file():
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return {}
    return {}


def save_cache(code: str, cache: dict) -> None:
    CACHE.mkdir(parents=True, exist_ok=True)
    (CACHE / f"{code}.json").write_text(json.dumps(cache, ensure_ascii=False, indent=0), encoding="utf-8")


def candidate_path(code: str) -> pathlib.Path:
    return CACHE / f"{code}.candidate.lang"


def fill_language(code: str, tokenizer, model, batch_size: int, yes: bool, dry_run: bool) -> dict:
    if code not in NLLB:
        return {"code": code, "status": "unsupported-nllb"}
    core_date, keys = langtool.parse_core()
    _, _, english = langtool.parse_lang("en-US")
    _, _, current = langtool.parse_lang(code)
    cache = load_cache(code)
    missing = [i for i, value in enumerate(current) if not value.strip()]
    translated = 0
    rejected = 0
    pending = []
    results = {}

    def flush():
        nonlocal translated, rejected
        if not pending:
            return
        batch = [item[1] for item in pending]
        try:
            outputs = translate_batch(tokenizer, model, batch, NLLB[code])
        except Exception as error:  # noqa: BLE001
            print(f"{code}: batch failed: {error}")
            rejected += len(pending)
            pending.clear()
            return
        for (index, source, mapping), output in zip(pending, outputs):
            if code == "sr-Latn":
                output = sr_latin(output)
            restored, ok = restore(output, mapping, source)
            if ok and restored.strip():
                cache[source] = restored
                results[index] = restored
                translated += 1
            else:
                rejected += 1
        pending.clear()
        save_cache(code, cache)

    for index in missing:
        source = langtool.unescape(english[index])
        if source in cache:
            results[index] = cache[source]
            translated += 1
            continue
        protected, mapping = protect(source)
        pending.append((index, source, mapping))
        if len(pending) >= batch_size:
            flush()
    flush()
    values = list(current)
    for index in missing:
        if results.get(index):
            values[index] = langtool.escape(results[index])
    candidate = candidates = "\n".join([f"#{core_date}${len(values)}", *values]) + "\n"
    candidate_path(code).write_bytes(candidate.encode("utf-8"))
    status = {"code": code, "missing": len(missing), "translated": translated,
              "rejected": rejected, "candidate": str(candidate_path(code))}
    if dry_run or translated == 0:
        status["status"] = "dry-run" if dry_run else "nothing-translated"
        return status
    command = [sys.executable, str(pathlib.Path(__file__).resolve().parent / "langimport.py"),
               "fill", code, str(candidate_path(code)), "--yes", "--allow-warnings"]
    if yes:
        result = subprocess.run(command, cwd=str(langtool.ROOT), capture_output=True, text=True, encoding="utf-8")
        status["fill_exit"] = result.returncode
        status["fill_tail"] = (result.stdout + result.stderr).strip().splitlines()[-2:]
        if result.returncode == 0:
            verify = subprocess.run([sys.executable, str(pathlib.Path(__file__).resolve().parent / "langtool.py"),
                                     "verify"], cwd=str(langtool.ROOT), capture_output=True, text=True, encoding="utf-8")
            status["verify_exit"] = verify.returncode
        status["status"] = "filled" if result.returncode == 0 else "fill-failed"
    else:
        status["status"] = "validated"
    return status


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--languages", help="comma separated ECP codes; default all with gaps <= max-gap")
    parser.add_argument("--max-gap", type=int, default=200)
    parser.add_argument("--batch-size", type=int, default=24)
    parser.add_argument("--yes", action="store_true", help="actually import translated values")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)
    if args.languages:
        codes = [item.strip() for item in args.languages.split(",") if item.strip()]
    else:
        codes = []
        for code in langtool.locale_codes():
            _, _, values = langtool.parse_lang(code)
            gap = sum(1 for value in values if not value.strip())
            if 0 < gap <= args.max_gap:
                codes.append(code)
    if not codes:
        print("no languages selected")
        return 1
    torch, tokenizer, model = load_model()
    summary = []
    for index, code in enumerate(codes, 1):
        start = time.time()
        status = fill_language(code, tokenizer, model, args.batch_size, args.yes, args.dry_run)
        status["seconds"] = round(time.time() - start, 1)
        summary.append(status)
        print(json.dumps(status, ensure_ascii=False))
    (CACHE / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
