"""Offline regression checks for Google-web transfer integrity.

All catalog files and workbooks used here live in temporary directories. These
tests do not import translations into the application's resources or use a
network service.
"""
from __future__ import annotations

import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
from xml.sax.saxutils import escape as xml_escape
import zipfile

import langtool
import web_transfer as transfer


SHEET_NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"


def scratch_directory(prefix: str):
    # The Windows app sandbox may expose a system TEMP directory whose child
    # files are not writable. Keep automatically removed fixtures in the
    # checkout's ignored scratch directory, away from Localization resources.
    root = Path(__file__).resolve().parents[2] / ".smoke" / "web-transfer-unittest"
    root.mkdir(parents=True, exist_ok=True)
    return tempfile.TemporaryDirectory(prefix=prefix, dir=root)


def inline_cell(coordinate: str, value: str) -> str:
    return (
        f'<c r="{coordinate}" t="inlineStr"><is><t xml:space="preserve">'
        f"{xml_escape(value)}</t></is></c>"
    )


def workbook(path: Path, rows: str, shared_strings: str | None = None) -> None:
    """Produce only the OOXML parts read by the offline importer."""
    with zipfile.ZipFile(path, "w") as archive:
        # Worksheet selection must not depend on which ZIP member appears first.
        archive.writestr("unrelated.xml", "<ignored/>")
        if shared_strings is not None:
            archive.writestr("xl/sharedStrings.xml", shared_strings)
        archive.writestr(
            "xl/worksheets/sheet1.xml",
            f'<worksheet xmlns="{SHEET_NS}"><sheetData>{rows}</sheetData></worksheet>',
        )


class ProtectionTests(unittest.TestCase):
    def test_round_trip_real_newlines_literal_escapes_and_repeated_slots(self):
        original = (
            "Use {0} twice: {0}; CPU cpu.usage; ${env:MY_API_KEY}; "
            "custom.<source>.<variable>; C:\\Tools\\ECP; literal \\n and \\\\;"
            "\nsecond line\r\nthird line"
        )
        masked, tokens = transfer.protect(original)
        self.assertNotIn("{0}", masked)
        self.assertNotIn("cpu.usage", masked)
        self.assertNotIn("${env:MY_API_KEY}", masked)
        self.assertNotIn("custom.<source>.<variable>", masked)
        self.assertNotIn("\n", masked)
        self.assertNotIn("\\n", masked)
        self.assertEqual(tokens.count("{0}"), 2)
        self.assertEqual(transfer.restore(masked, tokens), original)

    def test_missing_duplicate_unknown_and_damaged_markers_are_rejected(self):
        tokens = ["{0}", "cpu.usage"]
        invalid = {
            "missing": "ZXQ0000QXZ",
            "duplicate": "ZXQ0000QXZ ZXQ0001QXZ ZXQ0001QXZ",
            "unknown": "ZXQ0000QXZ ZXQ0001QXZ ZXQ9999QXZ",
            "damaged": "ZXQ0000QXZ ZXQ0001QXZ ZXQbrokenQXZ",
            "case-changed": "ZXQ0000QXZ zxq0001qxz",
        }
        for kind, text in invalid.items():
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                transfer.restore(text, tokens)

    def test_translation_may_reorder_intact_protected_tokens(self):
        self.assertEqual(
            transfer.restore("ZXQ0001QXZ 先於 ZXQ0000QXZ", ["{0}", "cpu.usage"]),
            "cpu.usage 先於 {0}",
        )

    def test_source_cannot_already_contain_sentinel_prefix(self):
        with self.assertRaises(ValueError):
            transfer.protect("An existing ZXQ0000QXZ would be ambiguous.")

    def test_code_blocks_and_inline_code_are_atomic(self):
        for code in [
            "`value = data.items[0].value`",
            "```json\n{\"enabled\": false, \"value\": 1}\n```",
        ]:
            with self.subTest(code=code):
                masked, tokens = transfer.protect("Example: " + code)
                self.assertIn(code, tokens)
                self.assertNotIn(code, masked)

    def test_callable_examples_preserve_arguments_and_punctuation(self):
        # Protecting only the function name lets a translator alter argument
        # names, ASCII commas or nested parentheses and produces broken code.
        for code in [
            "if(test, yes, no)",
            "number(value)",
            "round(number(value), 2)",
            "if(memory.usage >= 80, 'High', 'OK')",
        ]:
            with self.subTest(code=code):
                masked, tokens = transfer.protect("Example: " + code)
                self.assertIn(code, tokens)
                self.assertNotIn(code, masked)

    def test_pipeline_examples_are_atomic(self):
        for code in [
            "sub:start:length",
            "replace:old:new",
            "math:add/sub/mul/div/round/floor/ceil/abs",
            "time:<date format>",
            "time:HH:mm:ss",
            "time:relative",
            "auto:1 / gb:1",
        ]:
            with self.subTest(code=code):
                masked, tokens = transfer.protect("Format: " + code)
                self.assertIn(code, tokens)
                self.assertNotIn(code, masked)

    def test_natural_language_remains_available_to_translator(self):
        # Words shared with formatter names are not code in these contexts.
        prose = (
            "Show the public version number. Change the display duration. "
            "The feature was re-enabled. Download speed or upload speed."
        )
        masked, tokens = transfer.protect(prose)
        self.assertEqual(masked, prose)
        self.assertEqual(tokens, [])


class WorkbookTests(unittest.TestCase):
    def setUp(self):
        self.directory = scratch_directory(prefix="ecp-web-xlsx-")
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "translated.xlsx"

    def test_cells_and_rows_are_matched_by_coordinates_not_xml_order(self):
        # B precedes A, row 8 precedes row 3, and the physical row numbers differ
        # from the stable translation IDs. All are valid spreadsheet layouts.
        rows = (
            '<row r="8">'
            + inline_cell("B8", "second translated value")
            + '<c r="A8"><v>2</v></c></row>'
            + '<row r="3">'
            + inline_cell("B3", "first translated value")
            + '<c r="A3"><v>1</v></c></row>'
        )
        workbook(self.path, rows)
        self.assertEqual(
            transfer.read_xlsx(self.path),
            {1: "first translated value", 2: "second translated value"},
        )

    def test_shared_string_rich_text_and_inline_text(self):
        shared = (
            f'<sst xmlns="{SHEET_NS}"><si><r><t>first </t></r>'
            '<r><t>translation</t></r></si></sst>'
        )
        rows = (
            '<row r="2"><c r="A2"><v>1</v></c>'
            '<c r="B2" t="s"><v>0</v></c></row>'
            '<row r="5"><c r="A5"><v>2</v></c>'
            + inline_cell("B5", "second\ntranslation")
            + "</row>"
        )
        workbook(self.path, rows, shared)
        self.assertEqual(
            transfer.read_xlsx(self.path),
            {1: "first translation", 2: "second\ntranslation"},
        )

    def test_duplicate_stable_row_ids_are_rejected(self):
        rows = "".join(
            f'<row r="{row}"><c r="A{row}"><v>1</v></c>'
            + inline_cell(f"B{row}", text)
            + "</row>"
            for row, text in [(2, "one"), (3, "another")]
        )
        workbook(self.path, rows)
        with self.assertRaisesRegex(ValueError, "duplicate row ID"):
            transfer.read_xlsx(self.path)

    def test_duplicate_cell_coordinate_is_rejected_not_overwritten(self):
        rows = (
            '<row r="2"><c r="A2"><v>1</v></c>'
            + inline_cell("B2", "first")
            + inline_cell("B2", "silently substituted")
            + "</row>"
        )
        workbook(self.path, rows)
        with self.assertRaises(ValueError):
            transfer.read_xlsx(self.path)

    def test_cell_coordinate_cannot_point_to_another_row(self):
        rows = (
            '<row r="2"><c r="A2"><v>1</v></c>'
            + inline_cell("B3", "belongs to a different row")
            + "</row>"
        )
        workbook(self.path, rows)
        with self.assertRaises(ValueError):
            transfer.read_xlsx(self.path)

    def test_formula_is_rejected_instead_of_using_cached_value(self):
        workbook(
            self.path,
            '<row r="2"><c r="A2"><v>1</v></c>'
            '<c r="B2"><f>1+1</f><v>2</v></c></row>',
        )
        with self.assertRaisesRegex(ValueError, "formula"):
            transfer.read_xlsx(self.path)


class PackageCompatibilityTests(unittest.TestCase):
    def test_literal_text_becomes_translator_visible_shared_string(self):
        with scratch_directory('ecp-normalize-') as directory:
            source = Path(directory) / 'source.xlsx'
            output = Path(directory) / 'normalized.xlsx'
            literal = 'Show ZXQ0000QXZ and keep {0}; 한글'
            workbook(source, '<row r="2"><c r="A2"><v>1</v></c>'
                     '<c r="B2" t="str"><v>' + xml_escape(literal) + '</v></c></row>')
            with contextlib.redirect_stdout(io.StringIO()):
                transfer.normalize_xlsx(source, output)
            self.assertEqual(transfer.read_xlsx(source), transfer.read_xlsx(output))
            with zipfile.ZipFile(output) as archive:
                self.assertIn('xl/sharedStrings.xml', archive.namelist())
                self.assertIn(literal, archive.read('xl/sharedStrings.xml').decode('utf-8'))
                self.assertNotIn('t="str"', archive.read('xl/worksheets/sheet1.xml').decode('utf-8'))

    def test_existing_shared_strings_survive_additional_literal_conversion(self):
        with scratch_directory('ecp-normalize-') as directory:
            source = Path(directory) / 'source.xlsx'
            output = Path(directory) / 'normalized.xlsx'
            strings = f'<sst xmlns="{SHEET_NS}"><si><t>existing</t></si></sst>'
            workbook(source,
                     '<row r="2"><c r="A2"><v>1</v></c><c r="B2" t="s"><v>0</v></c></row>'
                     '<row r="3"><c r="A3"><v>2</v></c><c r="B3" t="str"><v>new</v></c></row>', strings)
            with contextlib.redirect_stdout(io.StringIO()):
                transfer.normalize_xlsx(source, output)
            self.assertEqual(transfer.read_xlsx(output), {1:'existing',2:'new'})


class ManifestFlowTests(unittest.TestCase):
    def setUp(self):
        self.directory = scratch_directory(prefix="ecp-web-catalog-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.core = self.root / "core.lang"
        self.paths = {code: self.root / f"{code}.lang" for code in ["en-US", "test"]}
        self.write_catalog(self.core, ["First {0}", "Kept", "Another {0}"])
        self.write_catalog(self.paths["en-US"], ["Show {0}", "Existing", "Show {0}"])
        self.write_catalog(self.paths["test"], ["", "既存", ""])
        for patcher in [
            mock.patch.object(transfer.langtool, "core_path", return_value=self.core),
            mock.patch.object(transfer.langtool, "lang_path", side_effect=self.paths.__getitem__),
            mock.patch.object(transfer.langtool, "source_tokens_map", return_value={}),
        ]:
            patcher.start()
            self.addCleanup(patcher.stop)
        self.manifest = self.root / "manifest.json"
        self.download = self.root / "download.txt"
        self.candidate = self.root / "candidate.lang"

    @staticmethod
    def write_catalog(path, values):
        path.write_text(
            "#20261020$3\n" + "\n".join(values) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    def export_and_translate(self):
        with contextlib.redirect_stdout(io.StringIO()):
            transfer.export("test", self.manifest, 0)
        doc = json.loads(self.manifest.read_text(encoding="utf-8"))
        self.download.write_text(
            "".join(
                f"QZROW{record['id']:04d}WORZ\n"
                + record["masked"].replace("Show", "示")
                + "\n\n"
                for record in doc["records"]
            ),
            encoding="utf-8",
        )
        return doc

    def consume(self):
        with contextlib.redirect_stdout(io.StringIO()):
            transfer.consume(self.manifest, self.download, self.candidate)
        return self.candidate.read_text(encoding="utf-8").split("\n")

    def test_deduplicated_source_fills_each_blank_and_keeps_existing_value(self):
        doc = self.export_and_translate()
        self.assertEqual(len(doc["records"]), 1)
        self.assertEqual(doc["records"][0]["indices"], [0, 2])
        before = {path: path.read_bytes() for path in [self.core, *self.paths.values()]}
        lines = self.consume()
        self.assertEqual(lines, ["#20261020$3", "示 {0}", "既存", "示 {0}", ""])
        self.assertEqual({path: path.read_bytes() for path in before}, before)

    def test_current_nonblank_value_is_not_overwritten_after_export(self):
        self.export_and_translate()
        self.write_catalog(self.paths["test"], ["", "既存", "別處已譯 {0}"])
        lines = self.consume()
        self.assertEqual(lines[1:4], ["", "既存", "別處已譯 {0}"])
        report = json.loads(self.candidate.with_suffix(".review.json").read_text(encoding="utf-8"))
        self.assertEqual(report["filledFields"], [])
        self.assertIn("filled after export", report["review"][0]["reason"])

    def test_unrelated_nonblank_edit_is_preserved_in_candidate(self):
        self.export_and_translate()
        self.write_catalog(self.paths["test"], ["", "後來修訂", ""])
        self.assertEqual(self.consume()[2], "後來修訂")

    def test_changed_core_with_same_date_and_count_is_rejected(self):
        self.export_and_translate()
        self.write_catalog(self.core, ["Another {0}", "Kept", "First {0}"])
        with self.assertRaisesRegex(SystemExit, "core changed"):
            self.consume()
        self.assertFalse(self.candidate.exists())

    def test_changed_english_source_is_rejected(self):
        self.export_and_translate()
        self.write_catalog(self.paths["en-US"], ["Changed {0}", "Existing", "Show {0}"])
        with self.assertRaisesRegex(SystemExit, "English source changed"):
            self.consume()
        self.assertFalse(self.candidate.exists())

    def test_missing_or_wrong_translation_row_is_rejected(self):
        for text in ["QZROW0002WORZ\n示 ZXQ0000QXZ\n", ""]:
            with self.subTest(text=text):
                self.export_and_translate() if not self.manifest.exists() else None
                self.download.write_text(text, encoding="utf-8")
                with self.assertRaises((SystemExit, ValueError)):
                    self.consume()
                self.assertFalse(self.candidate.exists())

    def test_duplicate_text_row_id_is_rejected(self):
        self.export_and_translate()
        self.download.write_text("QZROW0001WORZ\n甲\nQZROW0001WORZ\n乙\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate row ID"):
            self.consume()

    def test_unchanged_or_damaged_translation_goes_to_review_without_filling(self):
        doc = self.export_and_translate()
        for content in [doc["records"][0]["masked"], "示而缺槽"]:
            with self.subTest(content=content):
                candidate = self.root / ("unchanged.lang" if "Show" in content else "damaged.lang")
                self.download.write_text("QZROW0001WORZ\n" + content + "\n", encoding="utf-8")
                with contextlib.redirect_stdout(io.StringIO()):
                    transfer.consume(self.manifest, self.download, candidate)
                self.assertEqual(candidate.read_text(encoding="utf-8").split("\n")[1:4], ["", "既存", ""])
                report = json.loads(candidate.with_suffix(".review.json").read_text(encoding="utf-8"))
                self.assertEqual(len(report["review"]), 1)
                self.assertEqual(report["filledFields"], [])

    def test_restored_real_newline_and_literal_backslash_n_are_different(self):
        source = "Show {0}\nnext line; literal \\n"
        self.write_catalog(self.paths["en-US"], [langtool.escape(source), "Existing", "Show {0}"])
        doc = self.export_and_translate()
        self.assertEqual(len(doc["records"]), 2)
        lines = self.consume()
        self.assertEqual(len(lines), 5)
        self.assertEqual(langtool.unescape(lines[1]), source.replace("Show", "示"))
        self.assertIn("\\n", lines[1])
        self.assertIn("\\\\n", lines[1])


if __name__ == "__main__":
    unittest.main()
