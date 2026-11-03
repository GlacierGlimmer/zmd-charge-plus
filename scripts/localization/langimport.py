#!/usr/bin/env python3
"""Validate, import, fill and export plain-text `.lang` resources.

All modes operate on the date-version file header `#YYYYMMDD$N` and the single
shared field index. No network calls, no machine translation.

Commands:
    check <code> <file>                 validate a full translated file
    apply <code> <file> --yes           install a full translated file
    fill <code> <file> --yes            fill only blank target fields
    export <code> --out <file>          export a complete plain-text file
    export-missing <code> --out <file> --map <json> [--platform <name>]
    apply-missing <code> <file> --map <json> --yes
"""

from __future__ import annotations

import argparse
import datetime
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import langtool  # noqa: E402


def read_candidate(path: pathlib.Path) -> str:
    raw = path.read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        raise SystemExit(f"{path}: UTF-8 BOM is not allowed; save as UTF-8 without BOM.")
    if b"\r" in raw:
        raise SystemExit(f"{path}: CRLF/CR line endings are not allowed; save as LF.")
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise SystemExit(f"{path}: not valid UTF-8: {error}")


def split_lang(text: str, source: str) -> tuple[str, int, list[str]]:
    if not text.endswith("\n"):
        raise SystemExit(f"{source}: the file must end with one LF line ending.")
    lines = text.split("\n")
    lines.pop()
    if not lines:
        raise SystemExit(f"{source}: the file is empty.")
    try:
        date, declared = langtool.parse_header(lines[0], allow_legacy=False)
    except SystemExit as error:
        raise SystemExit(f"{source}: {error}")
    values = lines[1:]
    if len(values) != declared:
        raise SystemExit(f"{source}: header declares {declared} fields but the file contains {len(values)} lines.")
    return date, declared, values


def is_compatible(date: str, declared: int, core_date: str, core_count: int) -> tuple[bool, str]:
    if declared > core_count:
        return False, f"declares {declared} fields, core has {core_count}"
    if langtool.is_newer_date(date, core_date):
        return False, f"resource date {date} is newer than core {core_date}"
    if declared == core_count:
        return True, ""
    if declared not in langtool.recorded_field_counts():
        return False, f"field count {declared} is not a recorded prefix in field-history.json"
    return True, ""


def load_core():
    date, keys = langtool.parse_core()
    return date, keys


def existing_values(code: str) -> tuple[str, list[str]]:
    path = langtool.lang_path(code)
    if not path.is_file():
        return "", []
    try:
        date, _, values = langtool.parse_lang(code)
        return date, values
    except SystemExit:
        return "", []


def validate_values(code: str, keys: list[str], values: list[str], date: str, declared: int,
                    core_date: str, core_count: int, allow_warnings: bool,
                    skip_indices: set[int] | None = None) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    compatible, reason = is_compatible(date, declared, core_date, core_count)
    if not compatible:
        errors.append(f"incompatible field structure: {reason}")
    source_tokens = langtool.source_tokens_map()
    english_family = code in langtool.ENGLISH_FAMILY
    skip = skip_indices or set()
    for index, key in enumerate(keys[: len(values)]):
        if index in skip:
            continue
        value = values[index]
        if not value.strip():
            continue
        row_errors, row_warnings = langtool.placeholder_problems(langtool.unescape(key), value, source_tokens)
        if row_errors:
            errors.append(f"field #{index}: " + "; ".join(row_errors))
        if row_warnings:
            warnings.append(f"field #{index}: " + "; ".join(row_warnings))
        if not english_family and value.strip() == langtool.unescape(key).strip():
            warnings.append(f"field #{index}: identical to English source")
    if len(values) > 20 and all(not value.strip() for value in values):
        errors.append("the file has no translated content")
    return errors, warnings


def write_diff_report(code: str, mode: str, source: pathlib.Path, old_values: list[str],
                      new_values: list[str], errors: list[str], warnings: list[str]) -> pathlib.Path:
    report = langtool.ROOT / "docs" / "localization-reports" / (
        f"{mode}-{code}-{datetime.datetime.now().strftime('%Y%m%d-%H%M%S')}.md")
    report.parent.mkdir(parents=True, exist_ok=True)
    filled = changed = added = cleared = 0
    samples: list[str] = []
    for index in range(max(len(old_values), len(new_values))):
        old = old_values[index] if index < len(old_values) else None
        new = new_values[index] if index < len(new_values) else None
        if old is None and new:
            added += 1
        elif new is None:
            cleared += 1
        elif old == new:
            continue
        elif not (old or "").strip() and (new or "").strip():
            filled += 1
        else:
            changed += 1
        if len(samples) < 20 and old != new:
            samples.append(f"- #{index}: `{old}` -> `{new}`")
    lines = [
        f"# Language {mode} report: `{code}`",
        "",
        f"- Source file: `{source}`",
        f"- Generated: {datetime.datetime.now().isoformat(timespec='seconds')}",
        f"- Fields: {len(old_values)} -> {len(new_values)}",
        f"- Filled={filled}, changed={changed}, added={added}, cleared={cleared}",
        f"- Blocking errors: {len(errors)}; review warnings: {len(warnings)}",
        "",
        "## Blocking errors",
        *[f"- {item}" for item in errors[:50]],
        "",
        "## Review warnings",
        *[f"- {item}" for item in warnings[:50]],
        "",
        "## Sample changes",
        *samples,
        "",
    ]
    report.write_text("\n".join(lines), encoding="utf-8")
    return report


def backup_and_write(code: str, date: str, values: list[str]) -> str:
    path = langtool.lang_path(code)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    if path.is_file():
        backup = path.with_name(path.name + f".bak-{stamp}")
        backup.write_bytes(path.read_bytes())
        print(f"backup: {backup}")
    body = "\n".join([f"#{date}${len(values)}", *values]) + "\n"
    path.write_bytes(body.encode("utf-8"))
    digest = langtool.write_hash(path)
    print(f"written {code}: {len(values)} fields, {date}, sha256 {digest}")
    return digest


def command_check(code: str, path: pathlib.Path) -> int:
    core_date, keys = load_core()
    text = read_candidate(path)
    date, declared, values = split_lang(text, str(path))
    errors, warnings = validate_values(code, keys, values, date, declared, core_date, len(keys), False)
    report = write_diff_report(code, "check", path, existing_values(code)[1], values, errors, warnings)
    print(f"check {code}: {declared} fields, date {date}, errors={len(errors)}, warnings={len(warnings)}")
    print(f"report: {report}")
    for item in errors[:20]:
        print("  ERROR " + item)
    for item in warnings[:20]:
        print("  WARN  " + item)
    return 1 if errors else 0


def command_apply(code: str, path: pathlib.Path, allow_warnings: bool, yes: bool) -> int:
    core_date, keys = load_core()
    text = read_candidate(path)
    date, declared, values = split_lang(text, str(path))
    errors, warnings = validate_values(code, keys, values, date, declared, core_date, len(keys), allow_warnings)
    old_date, old_values = existing_values(code)
    if len(old_values) > declared and any(value.strip() for value in old_values[declared:]):
        errors.append("candidate would truncate existing non-empty translations beyond its field count")
    report = write_diff_report(code, "apply", path, old_values, values, errors, warnings)
    if errors:
        print(f"refusing to import: {len(errors)} blocking error(s); the existing file was not touched.")
        print(f"report: {report}")
        return 2
    if warnings and not allow_warnings:
        print(f"refusing to import: {len(warnings)} warning(s) require --allow-warnings after review.")
        print(f"report: {report}")
        return 3
    if not yes:
        print(f"dry run: validation passed; rerun with --yes to replace {langtool.lang_path(code).name}")
        print(f"report: {report}")
        return 0
    backup_and_write(code, date, values)
    print(f"report: {report}")
    return 0


def command_fill(code: str, path: pathlib.Path, allow_warnings: bool, yes: bool) -> int:
    core_date, keys = load_core()
    text = read_candidate(path)
    candidate_date, candidate_declared, candidate_values = split_lang(text, str(path))
    old_date, old_values = existing_values(code)
    if not old_values:
        raise SystemExit(f"{code}: target language file does not exist; use apply for a new language.")
    insertion_indices = {
        index for index in range(min(len(old_values), len(candidate_values), len(keys)))
        if not old_values[index].strip() and candidate_values[index].strip()
    }
    all_indices = set(range(min(len(old_values), len(candidate_values), len(keys))))
    errors, warnings = validate_values(code, keys, candidate_values, candidate_date,
                                       candidate_declared, core_date, len(keys), allow_warnings,
                                       skip_indices=all_indices - insertion_indices)
    merged = list(old_values)
    filled = 0
    limit = min(len(merged), len(candidate_values), len(keys))
    for index in range(limit):
        if not merged[index].strip() and candidate_values[index].strip():
            merged[index] = candidate_values[index]
            filled += 1
    if len(merged) < len(keys):
        merged += [""] * (len(keys) - len(merged))
    if len(merged) > len(keys):
        errors.append("target file declares more fields than core.lang")
    new_date = max(old_date, candidate_date) if len(old_date) == 8 else candidate_date
    report = write_diff_report(code, "fill", path, old_values, merged, errors, warnings)
    print(f"fill {code}: candidate {candidate_declared} fields, filled {filled} blank field(s)")
    if errors:
        print(f"refusing to write: {len(errors)} blocking error(s)")
        print(f"report: {report}")
        return 2
    if warnings and not allow_warnings:
        print(f"refusing to write: {len(warnings)} warning(s) require --allow-warnings after review.")
        print(f"report: {report}")
        return 3
    if not yes:
        print(f"dry run: rerun with --yes to write {langtool.lang_path(code).name}")
        print(f"report: {report}")
        return 0
    backup_and_write(code, new_date, merged)
    print(f"report: {report}")
    return 0


def command_export(code: str, out: pathlib.Path) -> int:
    path = langtool.lang_path(code)
    if not path.is_file():
        raise SystemExit(f"{code}: language file not found")
    if out.exists():
        raise SystemExit(f"refusing to overwrite existing export target: {out}")
    out.write_bytes(path.read_bytes())
    date, declared, _ = langtool.parse_lang(code)
    print(f"exported {code}: {declared} fields, date {date} -> {out}")
    return 0


def command_export_missing(code: str, out: pathlib.Path, map_path: pathlib.Path, platform: str | None) -> int:
    import json
    core_date, keys = load_core()
    _, english = existing_values("en-US")
    _, target_values = existing_values(code)
    if len(target_values) < len(keys):
        target_values = target_values + [""] * (len(keys) - len(target_values))
    indices: list[int] = []
    for index, key in enumerate(keys):
        if target_values[index].strip():
            continue
        if platform and not key.startswith(platform + "."):
            continue
        indices.append(index)
    if out.exists():
        raise SystemExit(f"refusing to overwrite existing export target: {out}")
    lines = [f"#{core_date}${len(indices)}", *(english[index] for index in indices)]
    out.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))
    mapping = {
        "format": "ecp-lang-missing-fields",
        "code": code,
        "resourceDate": core_date,
        "coreFieldCount": len(keys),
        "indices": indices,
        "fields": [keys[index] for index in indices],
        "platform": platform,
    }
    map_path.write_bytes((json.dumps(mapping, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))
    print(f"exported {len(indices)} missing field(s) for {code} -> {out}")
    print(f"mapping -> {map_path}")
    return 0


def command_apply_missing(code: str, path: pathlib.Path, map_path: pathlib.Path,
                          allow_warnings: bool, yes: bool) -> int:
    import json
    core_date, keys = load_core()
    mapping = json.loads(map_path.read_text(encoding="utf-8"))
    if mapping.get("format") != "ecp-lang-missing-fields":
        raise SystemExit("not a missing-field mapping file")
    if mapping.get("code") != code:
        raise SystemExit(f"mapping is for {mapping.get('code')!r}, not {code!r}")
    if int(mapping.get("coreFieldCount", -1)) != len(keys):
        raise SystemExit("mapping was created for a different core field count")
    indices = [int(index) for index in mapping.get("indices", [])]
    text = read_candidate(path)
    date, declared, values = split_lang(text, str(path))
    if declared != len(indices):
        raise SystemExit(f"translated file declares {declared} fields; mapping expects {len(indices)}")
    old_date, old_values = existing_values(code)
    merged = list(old_values) + [""] * max(0, len(keys) - len(old_values))
    errors: list[str] = []
    warnings: list[str] = []
    source_tokens = langtool.source_tokens_map()
    for position, index in enumerate(indices):
        if index >= len(keys):
            errors.append(f"mapping index {index} exceeds core.lang")
            continue
        if merged[index].strip():
            errors.append(f"field #{index} already has a translation; refusing to overwrite")
            continue
        value = values[position]
        if not value.strip():
            continue
        row_errors, row_warnings = langtool.placeholder_problems(langtool.unescape(keys[index]), value, source_tokens)
        errors.extend(f"field #{index}: {item}" for item in row_errors)
        warnings.extend(f"field #{index}: {item}" for item in row_warnings)
        merged[index] = value
    new_date = max(old_date, date) if len(old_date) == 8 else date
    report = write_diff_report(code, "apply-missing", path, old_values, merged, errors, warnings)
    if errors:
        print(f"refusing to write: {len(errors)} blocking error(s)")
        for item in errors[:20]:
            print("  ERROR " + item)
        print(f"report: {report}")
        return 2
    if warnings and not allow_warnings:
        print(f"refusing to write: {len(warnings)} warning(s) require --allow-warnings")
        print(f"report: {report}")
        return 3
    if not yes:
        print("dry run: rerun with --yes to write the merged language file")
        print(f"report: {report}")
        return 0
    backup_and_write(code, new_date, merged)
    print(f"report: {report}")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="ECP .lang import/export tooling")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("check", "apply", "fill", "apply-missing"):
        command = sub.add_parser(name)
        command.add_argument("code")
        command.add_argument("file", type=pathlib.Path)
        if name in ("apply", "fill", "apply-missing"):
            command.add_argument("--yes", action="store_true")
            command.add_argument("--allow-warnings", action="store_true")
        if name == "apply-missing":
            command.add_argument("--map", required=True, type=pathlib.Path)
    export = sub.add_parser("export")
    export.add_argument("code")
    export.add_argument("--out", required=True, type=pathlib.Path)
    missing = sub.add_parser("export-missing")
    missing.add_argument("code")
    missing.add_argument("--out", required=True, type=pathlib.Path)
    missing.add_argument("--map", required=True, type=pathlib.Path)
    missing.add_argument("--platform", choices=["windows", "linux", "macos", "android"])
    args = parser.parse_args(argv)
    if args.command == "check":
        return command_check(args.code, args.file)
    if args.command == "apply":
        return command_apply(args.code, args.file, args.allow_warnings, args.yes)
    if args.command == "fill":
        return command_fill(args.code, args.file, args.allow_warnings, args.yes)
    if args.command == "export":
        return command_export(args.code, args.out)
    if args.command == "export-missing":
        return command_export_missing(args.code, args.out, args.map, args.platform)
    if args.command == "apply-missing":
        return command_apply_missing(args.code, args.file, args.map, args.allow_warnings, args.yes)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
