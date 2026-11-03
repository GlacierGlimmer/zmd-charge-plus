"""Synchronize the unified `.lang` resources from the Windows maintenance checkout.

Rules (phase 3):
  * core.lang and core.lang.sha256 become byte-identical on all four platforms;
  * every `.lang`/`.lang.sha256` is copied from the maintenance source;
  * a target file that differs from the source is never silently overwritten:
    a safe upgrade is allowed only when the target values are a prefix of the
    source values (old date / shorter field set / appended platform fields);
    anything else is reported as a conflict and the sync stops;
  * no legacy JSON migration and no build-date rewriting happens here.
"""

import argparse
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]


def read_values(path: pathlib.Path):
    raw = path.read_text(encoding="utf-8")
    lines = raw.split("\n")
    if lines and lines[-1] == "":
        lines = lines[:-1]
    header, values = lines[0], lines[1:]
    declared = int(header.rsplit("$", 1)[1])
    if declared != len(values):
        raise ValueError(f"{path}: header declares {declared} but file has {len(values)} fields")
    return header, values


def safe_upgrade(target: pathlib.Path, source: pathlib.Path) -> bool:
    """True when target is an older/shorter version of source (no lost text)."""
    try:
        _, target_values = read_values(target)
        _, source_values = read_values(source)
    except (ValueError, OSError):
        return False
    if len(target_values) > len(source_values):
        return False
    native_renames = {}
    if source.name == "core.lang":
        import json
        import langtool
        metadata = source.parent / "platform-fields.json"
        if metadata.is_file():
            native_renames = {field["index"]: (langtool.escape(field["source"]), field["key"])
                              for field in json.loads(metadata.read_text(encoding="utf-8"))["fields"]}
    for index, value in enumerate(target_values):
        if value.strip() and value != source_values[index]:
            if native_renames.get(index) == (value, source_values[index]):
                continue
            return False
    return True


def copy_file(target: pathlib.Path, source: pathlib.Path, backup: pathlib.Path) -> bool:
    """Returns True when the file changed; raises on an unsafe overwrite."""
    if target.is_file():
        if target.read_bytes() == source.read_bytes():
            return False
        if target.name.endswith(".lang") and not safe_upgrade(target, source):
            raise SystemExit(
                f"Refusing to overwrite {target}: it differs from the maintenance source "
                f"({source}). Merge its unique translations into the Windows checkout first."
            )
        backup.mkdir(parents=True, exist_ok=True)
        shutil.copy2(target, backup / (target.parent.name + "-" + target.name))
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    return True


def sync(workspace: pathlib.Path) -> int:
    import datetime
    master = workspace / "zmd-charge-plus" / "Localization"
    assert workspace.is_absolute() and (master / "core.lang").is_file(), f"Missing maintenance source: {master}"
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = workspace / ".smoke" / f"localization-sync-backup-{stamp}"
    desktop = ["zmd-charge-plus-for-linux", "zmd-charge-plus-for-macos"]
    android_root = workspace / "zmd-charge-plus-for-android"
    android = android_root / "core" / "src" / "main" / "resources" / "localization"
    android_lang = android / "lang"
    assert all((workspace / repo).is_dir() for repo in desktop + ["zmd-charge-plus-for-android"])

    shared = sorted(master.glob("*.lang")) + sorted(master.glob("*.lang.sha256"))
    if (master / "platform-fields.json").is_file():
        shared.append(master / "platform-fields.json")
    changed = 0
    for repo in desktop:
        dest = workspace / repo / "Localization"
        for path in shared:
            if copy_file(dest / path.name, path, backup / repo):
                changed += 1
    android_lang.mkdir(parents=True, exist_ok=True)
    for path in shared:
        if copy_file(android_lang / path.name, path, backup / "android"):
            changed += 1
    for path in sorted(master.glob("*")):
        if not path.is_file() or path.name.startswith("."):
            continue
        # langimport backups are local recovery files, never Android resources.
        if ".bak-" in path.name:
            continue
        if path.name.endswith(".lang") or path.name.endswith(".lang.sha256"):
            continue
        if path.name == "reviewed-language-catalog.json":
            continue
        if copy_file(android / path.name, path, backup / "android"):
            changed += 1
    if (master / "reviewed-language-catalog.json").is_file():
        if copy_file(android / "catalog.json", master / "reviewed-language-catalog.json", backup / "android"):
            changed += 1
    for repo in desktop:
        target = workspace / repo / "Assets" / "Fonts"
        for path in (workspace / "zmd-charge-plus" / "Assets" / "Fonts").glob("*"):
            if path.is_file():
                target.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target / path.name)
    android_fonts = android_root / "app" / "src" / "main" / "assets" / "fonts"
    android_fonts.mkdir(parents=True, exist_ok=True)
    for path in (workspace / "zmd-charge-plus" / "Assets" / "Fonts").glob("*"):
        if path.is_file():
            shutil.copyfile(path, android_fonts / path.name)
    print(f"sync: {changed} file(s) updated; backups in {backup}")

    for repo in desktop:
        dest = workspace / repo / "Localization"
        for path in shared:
            if (dest / path.name).read_bytes() != path.read_bytes():
                raise SystemExit(f"Byte verification failed: {dest / path.name}")
    for path in shared:
        if (android_lang / path.name).read_bytes() != path.read_bytes():
            raise SystemExit(f"Byte verification failed: {android_lang / path.name}")
    import subprocess
    for repo in desktop + ["zmd-charge-plus-for-android"]:
        tool = workspace / repo / "scripts" / "localization" / "langtool.py"
        if tool.is_file():
            result = subprocess.run([sys.executable, str(tool), "verify"], cwd=workspace / repo,
                                    capture_output=True, text=True, encoding="utf-8")
            first = result.stdout.strip().splitlines()[0] if result.stdout.strip() else "verify failed"
            print(f"{repo}: {first}")
            if result.returncode != 0:
                raise SystemExit(result.stdout + result.stderr)
    print("Language resources synchronized and verified across four source checkouts.")
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("workspace", type=pathlib.Path)
    args = parser.parse_args()
    sys.exit(sync(args.workspace))
