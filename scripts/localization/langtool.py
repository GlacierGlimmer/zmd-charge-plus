#!/usr/bin/env python3
"""ECP Embedded Localization System tooling.

Maintains the line-mapped `.lang` resources described in the localization
task book:

    core.lang              #<version>$<field-count> + one key per line
    <code>.lang            #<version>$<field-count> + one value per line
    <code>.lang.sha256     lowercase SHA-256 of the exact .lang bytes

Commands:
    core      rebuild core.lang from the canonical English resource
    migrate   rebuild every existing locale .lang from the current JSON packs
    hash      write/refresh .sha256 files
    verify    verify headers, key order, line counts, escapes and SHA-256
    check     structural checks that do not compare hashes
    stats     per-locale version / translated / missing / completion report

No network access and no machine translation: values are only copied from the
already-authored locale packs.
"""

from __future__ import annotations

import argparse
import functools
import hashlib
import json
import pathlib
import re
import sys
from collections import OrderedDict

ROOT = pathlib.Path(__file__).resolve().parents[2]
LOCALIZATION = ROOT / "Localization"
if not LOCALIZATION.is_dir():
    LOCALIZATION = ROOT / "core" / "src" / "main" / "resources" / "localization"
LANG_DIR = LOCALIZATION
if (LOCALIZATION / "lang" / "core.lang").is_file():
    LANG_DIR = LOCALIZATION / "lang"
CATALOG = LOCALIZATION / "reviewed-language-catalog.json"
if not CATALOG.is_file():
    CATALOG = LOCALIZATION / "catalog.json"
REQUIREMENTS = LOCALIZATION / "reviewed-release-requirements.json"
CSHARP_PROJECT = ROOT / "EndfieldChargePlus.csproj"

HEADER_RE = re.compile(r"^#(\d{8})\$(\d+)$")
LEGACY_HEADER_RE = re.compile(r"^#(\d+\.\d+\.\d+)\$(\d+)$")
SHA_RE = re.compile(r"^[0-9a-f]{64}$")


def valid_date(value: str) -> bool:
    import datetime
    try:
        datetime.datetime.strptime(value, "%Y%m%d")
        return len(value) == 8
    except ValueError:
        return False


def parse_header(line: str, allow_legacy: bool = False) -> tuple[str, int]:
    match = HEADER_RE.match(line)
    if match:
        date, count = match.group(1), int(match.group(2))
        if not valid_date(date):
            raise SystemExit(f"Invalid resource date in header: {line!r}")
        return date, count
    if allow_legacy:
        legacy = LEGACY_HEADER_RE.match(line)
        if legacy:
            return legacy.group(1), int(legacy.group(2))
    raise SystemExit(f"Invalid header {line!r}; expected '#YYYYMMDD$field-count'")


def resource_date() -> str:
    raw = (LANG_DIR / "core.lang").read_text(encoding="utf-8")
    date, _ = parse_header(raw.split("\n")[0], allow_legacy=True)
    return date


def project_version() -> str:
    """Product version is independent of the language resource date."""
    if not CSHARP_PROJECT.is_file():
        return "unknown"
    text = CSHARP_PROJECT.read_text(encoding="utf-8-sig")
    match = re.search(r"<Version>\s*([^<\s]+)\s*</Version>", text)
    return match.group(1) if match else "unknown"


PLATFORM_FIELDS = [
    ("windows.title", "Endfield Charge Plus"),
    ("linux.title", "Endfield Charge Plus For Linux"),
    ("macos.title", "Endfield Charge Plus For MacOS"),
    ("android.title", "Endfield Charge Plus for Android"),
]
PLATFORM_FIELD_VALUES = dict(PLATFORM_FIELDS)
FIELD_HISTORY = LANG_DIR / "field-history.json"
if not FIELD_HISTORY.is_file():
    FIELD_HISTORY = LOCALIZATION / "field-history.json"


def field_history() -> dict:
    return read_json(FIELD_HISTORY, {"format": "ecp-lang-field-history", "entries": []}) or {"entries": []}


def record_field_history(date: str, keys: list[str]) -> None:
    document = field_history()
    digest = hashlib.sha256("\n".join(keys).encode("utf-8")).hexdigest()
    entries = [entry for entry in document.get("entries", [])
               if not (entry.get("date") == date and int(entry.get("count", -1)) == len(keys))]
    entries.append({"date": date, "count": len(keys), "fieldsSha256": digest, "fields": keys})
    entries.sort(key=lambda entry: str(entry.get("date")))
    document["entries"] = entries
    write_bytes(FIELD_HISTORY, json.dumps(document, ensure_ascii=False, indent=2) + "\n")


def recorded_field_counts() -> set[int]:
    return {int(entry["count"]) for entry in field_history().get("entries", [])}


def read_json(path: pathlib.Path, default=None):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except FileNotFoundError:
        return default


def catalog_entries() -> list[dict]:
    document = read_json(CATALOG)
    languages = document["languages"] if isinstance(document, dict) else document
    return sorted(languages, key=lambda item: item["id"])


def english_keys() -> list[str]:
    """Canonical field order sampled from the existing English pack."""
    english = read_json(LOCALIZATION / "en-US.json")
    if not isinstance(english, dict) or not english:
        raise SystemExit("en-US.json is required to build core.lang")
    keys = list(english.keys())
    requirements = read_json(REQUIREMENTS, {})
    required = list(requirements.get("requiredKeys", []))
    if core_path().is_file() and (LOCALIZATION / "platform-fields.json").is_file():
        _, existing = parse_core()
        known_sources = {source_key(key) for key in existing}
        additions = list(dict.fromkeys(key for key in keys + required if key not in known_sources and key not in existing))
        # Preserve the published positions and reviewed native field IDs. Never
        # append their old English aliases as duplicate fields when rebuilding.
        return existing + additions
    # Published fields keep the order they already have; only genuinely new
    # fields are appended, exactly like future core.lang revisions.
    ordered = list(keys)
    known = set(ordered)
    ordered += [key for key in required if key not in known]
    # Fields appended to core.lang by earlier revisions (for example platform
    # title fields) are not present in the legacy en-US JSON; keep them at the
    # end so re-running `core` can never drop or reorder published fields.
    existing_core = core_path()
    if existing_core.is_file():
        for line in existing_core.read_text(encoding="utf-8").split("\n")[1:]:
            if not line:
                continue
            key = unescape(line)
            if key not in known:
                ordered.append(key)
                known.add(key)
    seen = set()
    result = []
    for key in ordered:
        if key in seen:
            raise SystemExit(f"Duplicate core.lang key: {key!r}")
        if not key.strip():
            raise SystemExit("core.lang key is empty")
        seen.add(key)
        result.append(key)
    missing = [key for key in required if key not in seen]
    if missing:
        raise SystemExit(f"core.lang is missing {len(missing)} required keys, e.g. {missing[0]!r}")
    return result


def escape(value: str) -> str:
    value = value.replace("\r\n", "\n").replace("\r", "\n")
    return value.replace("\\", "\\\\").replace("\n", "\\n")


def unescape(value: str) -> str:
    out = []
    index = 0
    while index < len(value):
        char = value[index]
        if char == "\\" and index + 1 < len(value):
            nxt = value[index + 1]
            if nxt == "n":
                out.append("\n")
                index += 2
                continue
            if nxt == "\\":
                out.append("\\")
                index += 2
                continue
        out.append(char)
        index += 1
    return "".join(out)


def write_bytes(path: pathlib.Path, text: str) -> None:
    path.write_bytes(text.encode("utf-8"))


def sha256_of(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_hash(path: pathlib.Path) -> str:
    digest = sha256_of(path)
    write_bytes(path.with_name(path.name + ".sha256"), digest + "\n")
    return digest


def core_path() -> pathlib.Path:
    return LANG_DIR / "core.lang"


def lang_path(code: str) -> pathlib.Path:
    return LANG_DIR / f"{code}.lang"


def build_core(date: str | None = None) -> list[str]:
    keys = english_keys()
    if date is None:
        existing = core_path()
        date = parse_header(existing.read_text(encoding="utf-8").split("\n")[0], allow_legacy=True)[0] if existing.is_file() else ""
        if not valid_date(date):
            import datetime
            # Establish a baseline date only when the resource has no date header yet.
            date = datetime.datetime.now().strftime("%Y%m%d")
    if not valid_date(date):
        raise SystemExit(f"Invalid resource date: {date!r}")
    body = [f"#{date}${len(keys)}", *(escape(key) for key in keys)]
    write_bytes(core_path(), "\n".join(body) + "\n")
    write_hash(core_path())
    record_field_history(date, keys)
    return keys


def parse_core() -> tuple[str, list[str]]:
    raw = core_path().read_text(encoding="utf-8")
    lines = raw.split("\n")
    if lines and lines[-1] == "":
        lines = lines[:-1]
    if not lines:
        raise SystemExit("core.lang is empty")
    date, declared = parse_header(lines[0], allow_legacy=True)
    keys = [unescape(line) for line in lines[1:]]
    if declared != len(keys):
        raise SystemExit(
            f"core.lang declares {declared} fields but contains {len(keys)} lines"
        )
    if len(set(keys)) != len(keys):
        raise SystemExit("core.lang contains duplicate keys")
    for key in keys:
        if not key.strip():
            raise SystemExit("core.lang contains an empty key")
    return date, keys


@functools.lru_cache(maxsize=1)
def platform_source_fields() -> dict[str, str]:
    document = read_json(LOCALIZATION / "platform-fields.json", {}) or {}
    return {field["key"]: field["source"] for field in document.get("fields", [])}


def source_key(key: str) -> str:
    """Original English lookup ID for an indexed native-platform field."""
    return platform_source_fields().get(key, key)


def source_core_keys() -> tuple[str, list[str]]:
    date, keys = parse_core()
    return date, [source_key(key) for key in keys]


def locale_codes() -> list[str]:
    # The catalog is authoritative: legacy JSON packs may still exist for
    # languages that were intentionally removed from the shipped catalog.
    codes = [entry["code"] for entry in catalog_entries()]
    pattern = re.compile(r"^[a-z]{2,3}(?:-[A-Za-z0-9]+)*$")
    for path in sorted(LANG_DIR.glob("*.lang")):
        code = path.name[: -len(".lang")]
        if code == "core" or code in codes or not pattern.match(code):
            continue
        codes.append(code)
    return codes


def language_json(code: str) -> dict:
    data = read_json(LOCALIZATION / f"{code}.json", {})
    return data if isinstance(data, dict) else {}


def existing_lang_values(code: str) -> list[str]:
    path = lang_path(code)
    if not path.is_file():
        return []
    try:
        _, _, values = parse_lang(code)
        return values
    except SystemExit:
        return []


def render_locale(code: str, keys: list[str], date: str) -> str:
    legacy = language_json(code)
    current = existing_lang_values(code)
    body = [f"#{date}${len(keys)}"]
    for index, key in enumerate(keys):
        value = legacy.get(key)
        if value is None:
            # Fields that only exist in .lang (platform fields, manual edits)
            # keep their current translation instead of being cleared.
            existing = current[index] if index < len(current) else ""
            body.append(existing)
        else:
            body.append("" if value is None else escape(str(value)))
    return "\n".join(body) + "\n"


def migrate(force: bool = False) -> dict:
    """Build .lang files from the legacy JSON packs.

    Existing .lang files are the authoritative translation format after the
    migration: a differing file is never overwritten unless --force is given,
    and every overwrite keeps a timestamped backup first.
    """
    date, keys = parse_core()
    version = date
    result = {"written": [], "unchanged": [], "conflicts": []}
    import datetime
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    for code in locale_codes():
        if code == "core":
            continue
        rendered = render_locale(code, keys, version)
        target = lang_path(code)
        if target.is_file():
            current = target.read_text(encoding="utf-8")
            if current == rendered:
                result["unchanged"].append(code)
                continue
            if not force:
                result["conflicts"].append(code)
                continue
            target.with_name(target.name + f".bak-{stamp}").write_bytes(target.read_bytes())
        write_bytes(target, rendered)
        write_hash(target)
        result["written"].append(code)
    return result


def parse_lang(code: str) -> tuple[str, int, list[str]]:
    path = lang_path(code)
    raw = path.read_text(encoding="utf-8")
    if raw.startswith("\ufeff"):
        raise SystemExit(f"{path.name}: files must be UTF-8 without BOM")
    if "\r" in raw:
        raise SystemExit(f"{path.name}: files must use LF line endings")
    lines = raw.split("\n")
    if lines and lines[-1] == "":
        lines = lines[:-1]
    if not lines:
        raise SystemExit(f"{path.name}: empty file")
    date, declared = parse_header(lines[0], allow_legacy=True)
    values = lines[1:]
    if len(values) != declared:
        raise SystemExit(
            f"{path.name}: header declares {declared} fields, file has {len(values)} lines"
        )
    return date, declared, values


def is_newer_date(candidate: str, reference: str) -> bool:
    if len(candidate) == 8 and len(reference) == 8 and candidate.isdigit() and reference.isdigit():
        return int(candidate) > int(reference)
    def parts(value: str) -> tuple[int, ...]:
        return tuple(int(piece) for piece in value.split("."))
    try:
        return parts(candidate) > parts(reference)
    except ValueError:
        return False


def verify(stats_only: bool = False) -> int:
    version, keys = parse_core()
    recorded = recorded_field_counts()
    errors = []
    document = read_json(LOCALIZATION / "platform-fields.json", {}) or {}
    fields = document.get("fields", [])
    if len({field["key"] for field in fields}) != len(fields) or len({field["source"] for field in fields}) != len(fields):
        errors.append("duplicate platform field ID or legacy source")
    for field in fields:
        index = field["index"]
        if index < 0 or index >= len(keys) or keys[index] != field["key"]:
            errors.append("platform field index mismatch: " + field["key"])
    if len(set(source_key(key) for key in keys)) != len(keys):
        errors.append("platform aliases collide with shared source fields")
    core_digest = sha256_of(core_path())
    stored = (LANG_DIR / "core.lang.sha256").read_text(encoding="utf-8").strip()
    if stored != core_digest:
        errors.append("core.lang.sha256 does not match core.lang")
    checked = 0
    for code in locale_codes():
        path = lang_path(code)
        hash_path = path.with_name(path.name + ".sha256")
        if not path.is_file():
            errors.append(f"{code}: missing {path.name}")
            continue
        if not hash_path.is_file():
            errors.append(f"{code}: missing {hash_path.name}")
            continue
        checked += 1
        digest = sha256_of(path)
        if hash_path.read_text(encoding="utf-8").strip() != digest:
            errors.append(f"{code}: SHA-256 mismatch")
        try:
            file_version, declared, values = parse_lang(code)
        except SystemExit as error:
            errors.append(str(error))
            continue
        if declared > len(keys):
            errors.append(f"{code}: declares more fields than core.lang")
            continue
        if is_newer_date(file_version, version):
            errors.append(f"{code}: resource date {file_version} is newer than core {version}")
        if declared < len(keys) and declared not in recorded:
            errors.append(f"{code}: field prefix {declared} is not recorded in field-history.json")
        for index, value in enumerate(values):
            decoded = unescape(value)
            if "\r" in decoded:
                errors.append(f"{code}: field {index} contains CR")
    print(f"verify: {checked} locale files + core.lang checked, {len(errors)} error(s)")
    for error in errors:
        print("  " + error)
    return 1 if errors else 0


def check() -> int:
    errors = []
    try:
        _, keys = parse_core()
    except SystemExit as error:
        print(error)
        return 1
    if len(set(keys)) != len(keys):
        errors.append("duplicate core keys")
    if any(not key.strip() for key in keys):
        errors.append("empty core key")
    for code in locale_codes():
        path = lang_path(code)
        if not path.is_file():
            errors.append(f"{code}: missing language file")
            continue
        try:
            _, declared, values = parse_lang(code)
        except SystemExit as error:
            errors.append(str(error))
            continue
        if declared > len(keys):
            errors.append(f"{code}: declares {declared} > {len(keys)} core fields")
        if len(values) != declared:
            errors.append(f"{code}: line count mismatch")
        for index, value in enumerate(values[: len(keys)]):
            if re.search(r"(?<!\\)\\[^n\\]", value):
                errors.append(f"{code}: field {index} has an unsupported escape")
    print(f"check: {len(errors)} error(s)")
    for error in errors:
        print("  " + error)
    return 1 if errors else 0


def completion(code: str, keys: list[str], english: dict[str, str]) -> tuple[int, int]:
    _, _, values = parse_lang(code)
    translated = missing = 0
    for index, key in enumerate(keys):
        key = source_key(key)
        value = unescape(values[index]) if index < len(values) else ""
        if not value.strip():
            missing += 1
            continue
        if code in {"en-US", "en-AU", "en-CA", "en-GB", "en-NZ"}:
            translated += 1
            continue
        if value.strip() == english.get(key, "").strip():
            continue
        translated += 1
    return translated, missing


def stats() -> int:
    _, keys = parse_core()
    english = language_json("en-US")
    rows = []
    for code in locale_codes():
        path = lang_path(code)
        ok = "missing"
        if path.is_file():
            try:
                version, declared, values = parse_lang(code)
                digest = sha256_of(path)
                stored = path.with_name(path.name + ".sha256")
                ok = "ok" if stored.is_file() and stored.read_text(encoding="utf-8").strip() == digest else "hash-mismatch"
            except SystemExit:
                version, declared, ok = "?", len(keys), "invalid"
        else:
            version, declared = "?", 0
        translated, missing = completion(code, keys, english)
        rows.append((code, version, declared, translated, missing, ok))
    print(f"{'code':10} {'date':10} {'fields':7} {'translated':10} {'missing':8} {'completion':10} hash")
    for code, version, declared, translated, missing, ok in rows:
        total = len(keys)
        pct = (100.0 * translated / total) if total else 0.0
        print(f"{code:10} {version:8} {declared:<7} {translated:<10} {missing:<8} {pct:9.1f}% {ok}")
    print(f"total locales: {len(rows)}; core fields: {len(keys)}")
    return 0



LEGACY_ENUM = [
    ("zh-CN", "SimplifiedChinese"),
    ("en-US", "English"),
    ("zh-TW", "TraditionalChinese"),
    ("ja-JP", "Japanese"),
    ("ko-KR", "Korean"),
    ("de-DE", "German"),
    ("fr-FR", "French"),
    ("es-ES", "Spanish"),
    ("pt-BR", "Portuguese"),
    ("ru-RU", "Russian"),
    ("ar-SA", "Arabic"),
    ("hi-IN", "Hindi"),
    ("bn-BD", "Bengali"),
    ("ur-PK", "Urdu"),
    ("id-ID", "Indonesian"),
    ("vi-VN", "Vietnamese"),
    ("th-TH", "Thai"),
    ("tr-TR", "Turkish"),
    ("it-IT", "Italian"),
    ("fa-IR", "Persian"),
    ("bo-CN", "Tibetan"),
]
GENERATED_ENUM = LOCALIZATION.parent / "LocalizationManager.Languages.Generated.cs"


def enum_member_name(code: str) -> str:
    parts = []
    for chunk in code.replace("-", "_").replace(".", "_").split("_"):
        if not chunk:
            continue
        if chunk[0].isdigit():
            chunk = "N" + chunk
        parts.append(chunk[:1].upper() + chunk[1:])
    return "L" + "".join(parts)


def platform_upgrade(date: str) -> int:
    """One-time (idempotent) baseline upgrade to date headers plus platform fields.

    Existing field order is preserved; only missing platform title fields are
    appended. Existing non-empty translations are never replaced.
    """
    if not valid_date(date):
        raise SystemExit(f"Invalid resource date: {date!r}")
    old_date, keys = parse_core()
    new_keys = list(keys)
    for name, _ in PLATFORM_FIELDS:
        if name not in new_keys:
            new_keys.append(name)
    if new_keys[: len(keys)] != keys:
        raise SystemExit("Internal error: existing core order would change")
    changed = []
    for code in locale_codes():
        values = existing_lang_values(code)
        if len(values) < len(keys):
            values += [""] * (len(keys) - len(values))
        if len(values) > len(keys):
            values = values[: len(keys)]
        touched = len(values)
        for name, default in PLATFORM_FIELDS:
            index = new_keys.index(name)
            if index < len(values):
                if not values[index].strip():
                    values[index] = default
                    touched += 1
            else:
                values.append(default)
                touched += 1
        path = lang_path(code)
        body = "\n".join([f"#{date}${len(new_keys)}", *values]) + "\n"
        if path.is_file() and path.read_text(encoding="utf-8") == body:
            continue
        if path.is_file():
            path.with_name(path.name + ".phase3-bak").write_bytes(path.read_bytes())
        write_bytes(path, body)
        write_hash(path)
        changed.append(code)
    body = "\n".join([f"#{date}${len(new_keys)}", *(escape(key) for key in new_keys)]) + "\n"
    if core_path().read_text(encoding="utf-8") != body:
        write_bytes(core_path(), body)
        write_hash(core_path())
    if old_date and old_date != date:
        record_field_history(old_date, keys)
    record_field_history(date, new_keys)
    print(f"platform upgrade: core {len(keys)} -> {len(new_keys)} fields at {date}; {len(changed)} language file(s) updated")
    return 0


def generate_enum() -> int:
    legacy = {code: (index, name) for index, (code, name) in enumerate(LEGACY_ENUM)}
    members = []
    used = set()
    for entry in catalog_entries():
        code = entry["code"]
        ident = legacy.get(code, (None, None))[1] or enum_member_name(code)
        if ident in used:
            ident = ident + "X"
        used.add(ident)
        members.append(f"    {ident} = {entry['id']},")
    body = "\n".join(members)
    text = (
        "// <auto-generated>\n"
        "// Generated by scripts/localization/langtool.py genenum. Do not edit by hand.\n"
        "// Adding a locale only requires a catalog entry plus .lang/.lang.sha256 files.\n"
        "// </auto-generated>\n"
        "namespace EndfieldChargePlus;\n\n"
        "public enum AppLanguage\n{\n" + body + "\n}\n"
    )
    write_bytes(GENERATED_ENUM, text)
    print(f"generated {GENERATED_ENUM.name}: {len(members)} enum members")
    return 0


SLOT_RE = re.compile(r"\$\{[^{}\n]*\}|\{[^{}\n]*\}|\b[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_<>-]+)+")
NUMERIC_SLOT_RE = re.compile(r"\{[0-9]+\}")
ENGLISH_FAMILY = {"en-US", "en-AU", "en-CA", "en-GB", "en-NZ",
                  "en-x-pirate", "en-x-shakespeare", "en-x-anglish", "en-x-lolcat", "en-x-upside-down"}


def catalog_map() -> dict:
    return {entry["code"]: entry for entry in catalog_entries()}


def slot_tokens(text: str) -> list[str]:
    return [match.group() for match in SLOT_RE.finditer(text) if match.group() != "e.g"]


def placeholder_problems(key: str, value: str, source_tokens: dict) -> tuple[list[str], list[str]]:
    """Returns (blocking errors, review warnings).

    Numeric string.Format slots and unbalanced braces break rendering and are
    errors. Named documentation examples (custom.<source>.<variable>,
    ${env:...}) may be localized by hand, so a mismatch is a warning that the
    import tool only accepts with an explicit acknowledgement flag.
    """
    key = source_key(key)
    errors: list[str] = []
    warnings: list[str] = []
    if value.count("{") != value.count("}"):
        errors.append("unbalanced braces")
    expected = slot_tokens(key)
    actual = slot_tokens(value)
    allowed = set(expected) | set(source_tokens.get(key, {}))
    from collections import Counter
    missing = Counter(expected) - Counter(actual)
    for token in sorted(missing):
        if NUMERIC_SLOT_RE.fullmatch(token):
            errors.append("missing numeric slot: " + token)
        else:
            warnings.append("missing placeholder: " + token)
    extra = Counter(actual) - Counter(t for t in allowed)
    if extra and len(key) < 80:
        warnings.append("unexpected placeholders: " + ", ".join(sorted(extra)))
    if Counter(NUMERIC_SLOT_RE.findall(key)) != Counter(NUMERIC_SLOT_RE.findall(value)):
        errors.append("numeric slot mismatch")
    return errors, warnings


def source_tokens_map():
    requirements = read_json(REQUIREMENTS, {}) or {}
    return requirements.get("sourceTokens", {})


def locale_metrics(code: str, keys: list[str], core_version: str, source_tokens: dict) -> dict:
    path = lang_path(code)
    metrics = {"code": code, "version": "?", "declared": 0, "total": len(keys),
               "translated": 0, "identical_english": 0, "blank": 0,
               "placeholder_errors": 0, "placeholder_warnings": 0,
               "placeholder_samples": [], "hash_ok": False, "newer_than_core": False}
    if not path.is_file():
        metrics["blank"] = len(keys)
        return metrics
    try:
        file_version, declared, values = parse_lang(code)
    except SystemExit:
        metrics["declared"] = -1
        return metrics
    metrics["version"] = file_version
    metrics["declared"] = declared
    digest = sha256_of(path)
    sidecar = path.with_name(path.name + ".sha256")
    metrics["hash_ok"] = sidecar.is_file() and sidecar.read_text(encoding="utf-8").strip() == digest
    metrics["newer_than_core"] = is_newer_date(file_version, core_version)
    english = code in ENGLISH_FAMILY
    for index, key in enumerate(keys[: len(values)]):
        value = values[index]
        if not value.strip():
            metrics["blank"] += 1
            continue
        if english:
            metrics["translated"] += 1
        elif value.strip() == source_key(unescape(key)).strip():
            metrics["identical_english"] += 1
        else:
            metrics["translated"] += 1
        errors, warnings = placeholder_problems(unescape(key), value, source_tokens)
        if errors:
            metrics["placeholder_errors"] += 1
        if warnings:
            metrics["placeholder_warnings"] += 1
        if (errors or warnings) and len(metrics["placeholder_samples"]) < 3:
            metrics["placeholder_samples"].append({"index": index, "errors": errors, "warnings": warnings})
    metrics["blank"] += max(0, len(keys) - len(values))
    return metrics


def classify(metrics: dict) -> str:
    total = metrics["total"] or 1
    if metrics["translated"] == total and metrics["blank"] == 0:
        return "complete"
    if metrics["translated"] >= 2336:
        return "ui-complete"
    if metrics["translated"] > 0:
        return "partial"
    return "empty"


def audit(report_path: pathlib.Path | None = None) -> int:
    """Cross-check core.lang, migrations, hashes, versions and placeholders."""
    core_version, keys = parse_core()
    source_tokens = source_tokens_map()
    errors = []
    entries = catalog_entries()
    codes = [entry["code"] for entry in entries]
    # core.lang checks
    requirements = read_json(REQUIREMENTS, {}) or {}
    required = list(requirements.get("requiredKeys", []))
    canonical_keys = [source_key(key) for key in keys]
    missing_required = [key for key in required if key not in set(canonical_keys)]
    if missing_required:
        errors.append(f"core.lang is missing {len(missing_required)} required key(s), e.g. {missing_required[0]!r}")
    if len(set(keys)) != len(keys):
        errors.append("core.lang contains duplicate keys")
    if any(not key.strip() for key in keys):
        errors.append("core.lang contains an empty key")
    english_keys = list(language_json("en-US"))
    # Platform fields may be appended before a later shared UI extension.
    # Preserve every legacy key and its relative order without assuming a prefix.
    english_set = set(english_keys)
    legacy_order = [key for key in canonical_keys if key in english_set]
    if legacy_order != english_keys:
        errors.append("core.lang legacy field order differs from the canonical en-US pack")
    appended = keys[len(english_keys):]
    if len(set(appended)) != len(appended):
        errors.append("core.lang appended fields contain duplicates")
    # per-locale checks
    metrics = []
    langtool_recorded_counts = recorded_field_counts()
    legacy_mismatch = {}
    empty_pattern_mismatch = {}
    for code in codes:
        metric = locale_metrics(code, keys, core_version, source_tokens)
        metrics.append(metric)
        if metric["declared"] > len(keys):
            errors.append(f"{code}: declares {metric['declared']} fields, core.lang has {len(keys)}")
        if metric["declared"] < len(keys) and metric["declared"] not in langtool_recorded_counts:
            errors.append(f"{code}: field prefix {metric['declared']} is not recorded in field-history.json")
        if not metric["hash_ok"]:
            errors.append(f"{code}: missing or mismatched SHA-256")
        if metric["newer_than_core"]:
            errors.append(f"{code}: language version {metric['version']} is newer than core {core_version}")
        if metric["placeholder_errors"]:
            errors.append(f"{code}: {metric['placeholder_errors']} placeholder problem(s)")
        # Bootstrap JSON is historical after contextual .lang review. Record
        # drift for maintainers; the shipped .lang files remain authoritative.
        legacy = language_json(code)
        if legacy:
            try:
                _, _, values = parse_lang(code)
            except SystemExit:
                values = []
            mismatches = 0
            for index, key in enumerate(canonical_keys):
                if key not in legacy:
                    continue
                expected = legacy.get(key, "")
                actual = values[index] if index < len(values) else ""
                expected_encoded = "" if expected is None else escape(str(expected))
                if actual != expected_encoded:
                    mismatches += 1
            if mismatches:
                legacy_mismatch[code] = mismatches
            # empty-line positions must match fields missing from the legacy pack
            try:
                _, _, values = parse_lang(code)
            except SystemExit:
                values = []
            pattern_mismatch = 0
            for index, key in enumerate(canonical_keys[: len(values)]):
                if key not in legacy:
                    continue
                legacy_empty = not str(legacy.get(key, "")).strip()
                line_empty = not values[index].strip()
                if legacy_empty != line_empty:
                    pattern_mismatch += 1
            if pattern_mismatch:
                empty_pattern_mismatch[code] = pattern_mismatch
    payload = {
        "auditedAt": __import__("datetime").datetime.now().isoformat(timespec="seconds"),
        "coreVersion": core_version,
        "coreFields": len(keys),
        "locales": len(codes),
        "errors": errors,
        "legacyMismatch": legacy_mismatch,
        "emptyPatternMismatch": empty_pattern_mismatch,
        "metrics": metrics,
    }
    target = report_path or (ROOT / "docs" / "localization-reports" / "localization-audit.json")
    target.parent.mkdir(parents=True, exist_ok=True)
    write_bytes(target, json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
    print(f"audit: {len(codes)} locales, core {core_version} ({len(keys)} fields), {len(errors)} error(s)")
    print(f"audit report: {target}")
    for error in errors[:20]:
        print("  " + error)
    if len(errors) > 20:
        print(f"  ... {len(errors) - 20} more")
    return 1 if errors else 0


def completion_report() -> int:
    core_version, keys = parse_core()
    source_tokens = source_tokens_map()
    names = catalog_map()
    metrics = [locale_metrics(entry["code"], keys, core_version, source_tokens) for entry in catalog_entries()]
    for metric in metrics:
        metric["class"] = classify(metric)
    reports = ROOT / "docs" / "localization-reports"
    reports.mkdir(parents=True, exist_ok=True)
    stamp = __import__("datetime").datetime.now().strftime("%Y%m%d")
    csv_path = reports / f"completion-{stamp}.csv"
    md_path = reports / f"completion-{stamp}.md"
    header = "code,name,resource_date,total,translated,identical_english,blank,coverage,placeholder_errors,placeholder_warnings,hash_ok,class"
    lines = [header]
    for metric in metrics:
        entry = names.get(metric["code"], {})
        name = (entry.get("nativeName", "") + (" (" + entry.get("region", "") + ")" if entry.get("region") else "")).replace(",", " ")
        total = metric["total"] or 1
        coverage = f"{100.0 * metric['translated'] / total:.1f}%"
        lines.append(f"{metric['code']},{name},{metric['version']},{metric['total']},{metric['translated']},"
                     f"{metric['identical_english']},{metric['blank']},{coverage},{metric['placeholder_errors']},"
                     f"{metric['placeholder_warnings']},{str(metric['hash_ok']).lower()},{metric['class']}")
    write_bytes(csv_path, "\n".join(lines) + "\n")
    counts = {}
    for metric in metrics:
        counts[metric["class"]] = counts.get(metric["class"], 0) + 1
    md = ["# ECP 语言完成度统计", "",
          f"- 统计时间：{__import__('datetime').datetime.now().isoformat(timespec='seconds')}",
          f"- 核心版本：`{core_version}`；字段总数：**{len(keys)}**；语言数：**{len(metrics)}**",
          f"- 分类：complete={counts.get('complete', 0)}，ui-complete={counts.get('ui-complete', 0)}，"
          f"partial={counts.get('partial', 0)}，empty={counts.get('empty', 0)}",
          "- `translated` 为非空且与英语原文不同（英语族按其自身语言计）；`identical_english` 为与英语同形；"
          "`blank` 为空行（运行时回退英语），不计入译文。", "",
          "| 语言 | 名称 | 资源日期 | 字段 | 已翻译 | 英语同形 | 空白 | 覆盖率 | 占位符错误 | 占位符预警 | 校验 | 分类 |",
          "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |"]
    for metric in metrics:
        entry = names.get(metric["code"], {})
        name = (entry.get("nativeName", "") + (" (" + entry.get("region", "") + ")" if entry.get("region") else "")).replace("|", "/")
        total = metric["total"] or 1
        coverage = f"{100.0 * metric['translated'] / total:.1f}%"
        md.append(f"| `{metric['code']}` | {name} | {metric['version']} | {metric['total']} | {metric['translated']} | "
                  f"{metric['identical_english']} | {metric['blank']} | {coverage} | {metric['placeholder_errors']} | "
                  f"{metric['placeholder_warnings']} | {'OK' if metric['hash_ok'] else 'FAIL'} | {metric['class']} |")
    write_bytes(md_path, "\n".join(md) + "\n")
    print(f"report: {csv_path}")
    print(f"report: {md_path}")
    print("counts:", counts)
    return 0

def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="ECP .lang localization tooling")
    sub = parser.add_subparsers(dest="command", required=True)
    core_parser = sub.add_parser("core")
    core_parser.add_argument("--date", help="explicit resource date (YYYYMMDD); default keeps the current header date")
    migrate_parser = sub.add_parser("migrate")
    migrate_parser.add_argument("--force", action="store_true", help="overwrite differing .lang files after keeping a backup")
    migrate_parser.add_argument("--strict", action="store_true", help="exit nonzero when existing .lang files would be overwritten")
    sub.add_parser("hash")
    sub.add_parser("verify")
    sub.add_parser("check")
    sub.add_parser("stats")
    sub.add_parser("genenum")
    platform_parser = sub.add_parser("platform-upgrade")
    platform_parser.add_argument("--date", required=True, help="resource baseline date (YYYYMMDD)")
    sub.add_parser("audit")
    sub.add_parser("report")
    args = parser.parse_args(argv)
    if args.command == "core":
        keys = build_core(args.date)
        print(f"core.lang: {len(keys)} fields, resource date {resource_date()}")
        return 0
    if args.command == "platform-upgrade":
        return platform_upgrade(args.date)
    if args.command == "migrate":
        result = migrate(force=args.force)
        print(f"migrate: wrote {len(result['written'])} new/forced, kept {len(result['unchanged'])} unchanged, "
              f"{len(result['conflicts'])} existing file(s) left untouched")
        for code in result["conflicts"]:
            print(f"  protected existing translation: {code}.lang (use --force only for an intentional re-migration)")
        if args.strict and result["conflicts"]:
            return 2
        return 0
    if args.command == "hash":
        write_hash(core_path())
        for code in locale_codes():
            if lang_path(code).is_file():
                write_hash(lang_path(code))
        print("hash: refreshed all SHA-256 files")
        return 0
    if args.command == "verify":
        return verify()
    if args.command == "check":
        return check()
    if args.command == "stats":
        return stats()
    if args.command == "genenum":
        return generate_enum()
    if args.command == "audit":
        return audit()
    if args.command == "report":
        return completion_report()
    parser.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
