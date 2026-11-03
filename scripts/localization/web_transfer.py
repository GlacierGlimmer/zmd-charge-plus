#!/usr/bin/env python3
"""Offline transfer files for the free Google Translate document website.

This tool never sends network requests. Export JSON is converted to XLSX by the
companion builder; a downloaded XLSX is checked before producing a fill candidate.
Existing non-empty translations and the frozen core are never written here.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

import langtool

FORMAT = "ecp-google-web-transfer-v1"
NS = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
# Order longest constructs first. A sentinel represents one exact occurrence,
# so repeated placeholders, literal newlines and backslashes cannot be lost.
PROTECTED = re.compile(
    r"```[\s\S]*?```|`[^`\n]+`|\$\{[^{}\n]+\}|\{[^{}\n]+\}|"
    r"https?://[^\s<>]+|(?:[A-Za-z]:\\|/usr/|/proc/|/sys/|~/)[^\s,;]+|"
    r"custom\.<[^>]+>\.(?:<[^>]+>|[A-Za-z_*][\w*-]*)|\b[A-Za-z_][\w-]*(?:\.[\w*-]+)+\b|"
    r"#[0-9a-fA-F]{6,8}\b|\\[nrt\\]|\r\n|\n|\\|"
    r"\b(?:Endfield Charge Plus(?: For Linux| For MacOS| for Android)?|"
    r"LibreHardwareMonitor|PawnIO|AIDA64|DeepSeek|PresentMon|NativeIsland|"
    r"CPU-Z|HTTP|HTTPS|JSON|WMI|API|CPU|GPU|HUD|FPS|UTF-8|UAC|UTC|DST|"
    r"ICMP|TCP|UDP|IPv4|IPv6|Ctrl|Alt|Shift|F\d{1,2}|"
    r"clamp|round|floor|ceil|abs|sign|sqrt|pow|log10|log|exp|sin|cos|tan|"
    r"percent|between|len|contains|startswith|endswith|upper|lower|concat|"
    r"isnull|isempty|isnan|min|max|avg|sum|auto|"
    r"duration-long|true|false|"
    r"MB/s|Mbps|GiB|GHz|kHz|MHz|KiB|MiB|TiB)\b"
    r"|\b[A-Za-z_][\w]*(?:\[\d+\])(?:\.[A-Za-z_][\w]*(?:\[\d+\])?)+"
)
MARKER = re.compile(r"ZXQ\d{4}QXZ")
ROW_MARKER = re.compile(r"QZROW(\d{4})WORZ")
CALL = re.compile(r"\b(?:if|number|string|bool|clamp|round|floor|ceil|abs|sign|sqrt|pow|log10|log|exp|sin|cos|tan|percent|between|len|contains|startswith|endswith|upper|lower|concat|isnull|isempty|isnan|min|max|avg|sum)\s*\(")
PIPELINE = re.compile(
    r"\b(?:sub:[\w-]+:[\w-]+|replace:[\w-]+:[\w-]+|"
    r"math:[A-Za-z]+(?:/[A-Za-z]+)*|time:(?:<[^>\n]+>|[A-Za-z:]+)|"
    r"(?:auto|kb|mb|gb|tb):(?:\d+|n)(?:\s*/\s*(?:auto|kb|mb|gb|tb):(?:\d+|n))*|"
    r"(?:bytes|speed|duration-long|duration|upper|lower)(?:\s*/\s*(?:bytes|speed|duration-long|duration|upper|lower))+|"
    r"number,\s*string\s+or\s+bool|test\s*\?\s*yes\s*:\s*no|and/or/not)"
)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def protect(text):
    if "ZXQ" in text:
        raise ValueError("source collides with sentinel prefix")
    spans = [(m.start(), m.end()) for regex in [PROTECTED, PIPELINE] for m in regex.finditer(text)]
    for match in CALL.finditer(text):
        depth, quote, escaped = 1, None, False
        for pos in range(match.end(), len(text)):
            char = text[pos]
            if quote:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == quote:
                    quote = None
            elif char in "\"'":
                quote = char
            elif char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
                if depth == 0:
                    spans.append((match.start(), pos + 1))
                    break
            elif char == "\n":
                break
    # Outer code examples win over tokens inside them. Operate on the original
    # text once; masking a nested token first would change its containing span.
    tokens, pieces, cursor = [], [], 0
    for start, end in sorted(spans, key=lambda pair: (pair[0], -pair[1])):
        if start < cursor:
            continue
        pieces.extend([text[cursor:start], f"ZXQ{len(tokens):04d}QXZ"])
        tokens.append(text[start:end])
        cursor = end
    pieces.append(text[cursor:])
    return "".join(pieces), tokens


def restore(text, tokens):
    expected = collections.Counter(f"ZXQ{i:04d}QXZ" for i in range(len(tokens)))
    if collections.Counter(MARKER.findall(text)) != expected:
        raise ValueError("missing, duplicated or unknown protected token")
    restored = MARKER.sub(lambda m: tokens[int(m.group()[3:7])], text)
    if "ZXQ" in restored:
        raise ValueError("damaged protected token")
    return restored


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def export(code, out, limit):
    date, keys = langtool.parse_core()
    _, _, english = langtool.parse_lang("en-US")
    _, _, target = langtool.parse_lang(code)
    groups = {}
    for index in range(len(keys)):
        if target[index].strip():
            continue
        source = langtool.unescape(english[index])
        if not source.strip():
            raise SystemExit(f"English source #{index} is empty")
        groups.setdefault(source, []).append(index)
    records = []
    for source, indices in groups.items():
        if limit and len(records) >= limit:
            break
        masked, tokens = protect(source)
        records.append({"id": len(records) + 1, "indices": indices,
                        "source": source, "masked": masked, "tokens": tokens})
    document = {"format": FORMAT, "locale": code, "resourceDate": date,
                "coreCount": len(keys), "coreSha256": sha(langtool.core_path()),
                "targetSha256": sha(langtool.lang_path(code)),
                "sourceSha256": sha(langtool.lang_path("en-US")),
                "provider": "Google Translate free website",
                "records": records}
    if out.exists():
        raise SystemExit(f"refusing to overwrite {out}")
    write_json(out, document)
    print(json.dumps({"locale": code, "uniqueRows": len(records),
                      "blankFields": sum(len(r["indices"]) for r in records),
                      "maskedCharacters": sum(len(r["masked"]) for r in records),
                      "manifest": str(out)}, ensure_ascii=False))


def read_xlsx(path):
    """Read actual cell coordinates, never zip source rows to translated rows."""
    with zipfile.ZipFile(path) as z:
        if sum(i.file_size for i in z.infolist()) > 50_000_000:
            raise ValueError("downloaded workbook is unexpectedly large")
        strings = []
        if "xl/sharedStrings.xml" in z.namelist():
            root = ET.fromstring(z.read("xl/sharedStrings.xml"))
            strings = ["".join(t.text or "" for t in si.findall(".//s:t", NS)) for si in root.findall("s:si", NS)]
        sheets = [n for n in z.namelist() if re.fullmatch(r"xl/worksheets/sheet\d+\.xml", n)]
        if len(sheets) != 1:
            raise ValueError("expected exactly one worksheet")
        result = {}
        seen = set()
        for row in ET.fromstring(z.read(sheets[0])).findall("s:sheetData/s:row", NS):
            cells = {}
            for cell in row.findall("s:c", NS):
                coordinate = cell.attrib.get("r", "")
                match = re.fullmatch(r"([A-Z]+)([1-9][0-9]*)", coordinate)
                if not match or match.group(2) != row.attrib.get("r"):
                    raise ValueError("invalid or cross-row cell coordinate")
                if coordinate in seen:
                    raise ValueError("duplicate cell coordinate")
                seen.add(coordinate)
                col = match.group(1)
                if cell.find("s:f", NS) is not None:
                    raise ValueError("unexpected formula in translation workbook")
                value = cell.findtext("s:v", default="", namespaces=NS)
                if cell.attrib.get("t") == "s":
                    value = strings[int(value)]
                elif cell.attrib.get("t") == "inlineStr":
                    value = "".join(t.text or "" for t in cell.findall("s:is//s:t", NS))
                cells[col] = value
            if not cells.get("A", "").isdigit():
                continue  # Only the header has a nonnumeric ID.
            row_id = int(cells["A"])
            if row_id in result:
                raise ValueError(f"duplicate row ID {row_id}")
            result[row_id] = cells.get("B", "")
        return result


def normalize_xlsx(source, output):
    """Repair the OOXML content-types declaration emitted by the JS exporter.

    Worksheet values/styles are copied byte-for-byte. Some readers tolerate a
    workbook MIME type used as the XML default; Google rejects that packaging.
    """
    if source.resolve() == output.resolve() or output.exists():
        raise SystemExit("normalization needs a separate, nonexistent output")
    namespace = "http://schemas.openxmlformats.org/package/2006/content-types"
    ET.register_namespace("", namespace)
    with zipfile.ZipFile(source) as original, zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as fixed:
        string_namespace = NS['s']
        shared_path = 'xl/sharedStrings.xml'
        shared = ET.fromstring(original.read(shared_path)) if shared_path in original.namelist() else ET.Element(f'{{{string_namespace}}}sst')
        strings = [''.join(t.text or '' for t in si.findall('.//s:t', NS)) for si in shared.findall('s:si', NS)]
        string_indices = {value: index for index, value in enumerate(strings)}
        string_references = 0
        for item in original.infolist():
            if item.filename == shared_path:
                continue
            data = original.read(item.filename)
            if re.fullmatch(r'xl/worksheets/sheet\d+\.xml', item.filename):
                worksheet = ET.fromstring(data)
                for cell in worksheet.findall('.//s:c', NS):
                    # Exporter used formula-result strings (t=str) for literals.
                    # Excel displays these, but document translators only visit
                    # real string-table cells. Normalize without changing text.
                    if cell.get('t') == 'str' and cell.find('s:f', NS) is None:
                        value = cell.find('s:v', NS)
                        text = value.text or '' if value is not None else ''
                        if text not in string_indices:
                            string_indices[text] = len(strings)
                            strings.append(text)
                            si = ET.SubElement(shared, f'{{{string_namespace}}}si')
                            element = ET.SubElement(si, f'{{{string_namespace}}}t')
                            element.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
                            element.text = text
                        if value is None:
                            value = ET.SubElement(cell, f'{{{string_namespace}}}v')
                        cell.set('t', 's')
                        value.text = str(string_indices[text])
                    if cell.get('t') == 's':
                        string_references += 1
                data = ET.tostring(worksheet, encoding='utf-8', xml_declaration=True)
            if item.filename == "[Content_Types].xml":
                root = ET.fromstring(data)
                for element in root:
                    if element.tag == f"{{{namespace}}}Default" and element.get("Extension") == "xml":
                        element.set("ContentType", "application/xml")
                main = next((el for el in root if el.get("PartName") == "/xl/workbook.xml"), None)
                if main is None:
                    main = ET.SubElement(root, f"{{{namespace}}}Override", {"PartName": "/xl/workbook.xml"})
                main.set("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")
                data = ET.tostring(root, encoding="utf-8", xml_declaration=True)
            fixed.writestr(item.filename, data)
        shared.set('count', str(string_references))
        shared.set('uniqueCount', str(len(strings)))
        fixed.writestr(shared_path, ET.tostring(shared, encoding='utf-8', xml_declaration=True))
    if read_xlsx(source) != read_xlsx(output):
        raise SystemExit("unexpected workbook data change during normalization")
    print(f"normalized package without changing cells: {output}")


def text_batches(manifest_path, directory, max_chars):
    doc = json.loads(manifest_path.read_text(encoding="utf-8"))
    if doc.get("format") != FORMAT:
        raise SystemExit("unrecognized transfer manifest")
    directory.mkdir(parents=True, exist_ok=False)
    batches, current = [], ""
    for record in doc["records"]:
        block = f"QZROW{record['id']:04d}WORZ\n{record['masked']}\n\n"
        if len(block) > max_chars:
            raise SystemExit(f"row {record['id']} exceeds safe website text limit")
        if len(current) + len(block) > max_chars:
            batches.append(current)
            current = ""
        current += block
    if current:
        batches.append(current)
    for number, text in enumerate(batches, 1):
        (directory / f"batch-{number:03d}.txt").write_text(text, encoding="utf-8", newline="\n")
    write_json(directory / "batch-map.json", {"manifest": str(manifest_path.resolve()),
               "locale": doc["locale"], "batchCount": len(batches),
               "rows": len(doc["records"]), "limit": max_chars})
    print(json.dumps({"batches": len(batches), "lengths": [len(s) for s in batches], "out": str(directory)}))


def read_text_rows(path):
    text = path.read_text(encoding="utf-8")
    matches = list(ROW_MARKER.finditer(text))
    if not matches or text[:matches[0].start()].strip():
        raise ValueError("translation does not start with an exact row marker")
    result = {}
    for pos, match in enumerate(matches):
        row_id = int(match.group(1))
        if row_id in result:
            raise ValueError(f"duplicate row ID {row_id}")
        end = matches[pos+1].start() if pos+1 < len(matches) else len(text)
        result[row_id] = text[match.end():end].strip()
    return result


def consume(manifest_path, workbook_path, out, provider=None):
    doc = json.loads(manifest_path.read_text(encoding="utf-8"))
    if doc.get("format") != FORMAT:
        raise SystemExit("unrecognized transfer manifest")
    code = doc["locale"]
    date, keys = langtool.parse_core()
    if date != doc["resourceDate"] or len(keys) != doc["coreCount"] or sha(langtool.core_path()) != doc["coreSha256"]:
        raise SystemExit("core changed since export")
    if sha(langtool.lang_path("en-US")) != doc["sourceSha256"]:
        raise SystemExit("English source changed since export")
    _, _, old = langtool.parse_lang(code)
    rows = read_text_rows(workbook_path) if workbook_path.suffix == ".txt" else read_xlsx(workbook_path)
    if set(rows) != {r["id"] for r in doc["records"]}:
        raise SystemExit("translated row IDs differ from manifest; refusing positional guessing")
    values = list(old)
    review = []
    filled = []
    source_tokens = langtool.source_tokens_map()
    for record in doc["records"]:
        if not record['indices']:
            continue  # A shared whole-file batch may contain rows this locale already has.
        try:
            text = restore(rows[record["id"]], record["tokens"])
            if not text.strip() or text == record["source"]:
                raise ValueError("empty or unchanged source; needs manual review")
            for index in record["indices"]:
                if old[index].strip():
                    raise ValueError(f"target field #{index} was filled after export")
                errors, warnings = langtool.placeholder_problems(langtool.unescape(keys[index]), langtool.escape(text), source_tokens)
                if errors or warnings:
                    raise ValueError("; ".join(errors + warnings))
            for index in record["indices"]:
                values[index] = langtool.escape(text)
                filled.append(index)
        except ValueError as error:
            review.append({"id": record["id"], "indices": record["indices"], "reason": str(error),
                           "source": record["source"], "returned": rows[record["id"]]})
    if out.exists():
        raise SystemExit(f"refusing to overwrite {out}")
    out.write_bytes(("\n".join([f"#{date}${len(keys)}", *values]) + "\n").encode("utf-8"))
    report = {"locale": code, "source": str(workbook_path), "sourceSha256": sha(workbook_path),
              "provider": provider or doc["provider"], "inputMode": "text UI" if workbook_path.suffix == ".txt" else "document UI", "candidateOnly": True,
              "filledFields": filled, "review": review,
              "semanticReviewRequired": True, "existingValuesChanged": 0}
    write_json(out.with_suffix(".review.json"), report)
    print(json.dumps({"locale": code, "candidateFields": len(filled), "reviewRows": len(review),
                      "candidate": str(out)}, ensure_ascii=False))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="command", required=True)
    e = sub.add_parser("export")
    e.add_argument("code")
    e.add_argument("--out", type=pathlib.Path, required=True)
    e.add_argument("--limit", type=int, default=0)
    b = sub.add_parser("text-batches")
    b.add_argument("manifest", type=pathlib.Path)
    b.add_argument("--out", type=pathlib.Path, required=True)
    b.add_argument("--max-chars", type=int, default=4400)
    n = sub.add_parser("normalize-xlsx")
    n.add_argument("source", type=pathlib.Path)
    n.add_argument("output", type=pathlib.Path)
    i = sub.add_parser("consume")
    i.add_argument("manifest", type=pathlib.Path)
    i.add_argument("workbook", type=pathlib.Path)
    i.add_argument("--out", type=pathlib.Path, required=True)
    i.add_argument("--provider", help="Actual translation service used (for provenance only)")
    a = p.parse_args()
    if a.command == "export":
        export(a.code, a.out, a.limit)
    elif a.command == "text-batches":
        text_batches(a.manifest, a.out, a.max_chars)
    elif a.command == "normalize-xlsx":
        normalize_xlsx(a.source, a.output)
    else:
        consume(a.manifest, a.workbook, a.out, a.provider)


if __name__ == "__main__":
    main()
