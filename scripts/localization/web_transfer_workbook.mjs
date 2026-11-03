// Intermediate file for the free Google Translate website; no network calls.
import fs from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
// Windows may prohibit junction creation. Resolve the read-only bundled package
// with Node's normal package resolver instead of copying or changing dependencies.
const requireBundled = createRequire(path.join(process.env.ECP_ARTIFACT_NODE_MODULES ?? path.resolve('node_modules'), '..', 'ecp-local-loader.cjs'));
const { Workbook, SpreadsheetFile } = await import(pathToFileURL(requireBundled.resolve('@oai/artifact-tool')).href);

const [manifestPath, outputPath, previewPath] = process.argv.slice(2);
if (!manifestPath || !outputPath) throw new Error('Usage: builder manifest.json output.xlsx [preview.png]');
const manifest = JSON.parse(await fs.readFile(manifestPath, 'utf8'));
if (manifest.format !== 'ecp-google-web-transfer-v1') throw new Error('Invalid transfer manifest');
await fs.access(outputPath).then(() => { throw new Error('Refusing to overwrite output'); }, () => {});
const wb = Workbook.create();
const sheet = wb.worksheets.add('Translation');
const rows = [['ID', 'Text'], ...manifest.records.map(r => [r.id, r.masked])];
sheet.getRange(`A1:B${rows.length}`).values = rows;
sheet.showGridLines = false;
sheet.getRange(`A1:B${rows.length}`).format.font.name = 'Arial';
sheet.getRange(`A1:B${rows.length}`).format.font.size = 10;
sheet.getRange(`A1:B${rows.length}`).format.verticalAlignment = 'top';
sheet.getRange(`B1:B${rows.length}`).format.wrapText = true;
sheet.getRange(`A1:A${rows.length}`).format.columnWidth = 8;
sheet.getRange(`B1:B${rows.length}`).format.columnWidth = 112;
sheet.getRange('A1:B1').format.fill = '#243447';
sheet.getRange('A1:B1').format.font.color = '#FFFFFF';
sheet.getRange('A1:B1').format.font.bold = true;
sheet.freezePanes.freezeRows(1);
sheet.getRange(`A2:A${rows.length}`).setNumberFormat('0');
sheet.getRange(`A1:B${rows.length}`).format.autofitRows();
wb.recalculate();
const check = await wb.inspect({kind:'table',range:`Translation!A1:B${Math.min(rows.length,4)}`,include:'values,formulas',tableMaxRows:4,tableMaxCols:2,tableMaxCellChars:90});
console.log(check.ndjson);
await fs.mkdir(path.dirname(outputPath), {recursive:true});
if (previewPath) {
  const preview = await wb.render({sheetName:'Translation',range:`A1:B${Math.min(rows.length,5)}`,scale:1,format:'png'});
  await fs.writeFile(previewPath,new Uint8Array(await preview.arrayBuffer()));
}
const file = await SpreadsheetFile.exportXlsx(wb);
await file.save(outputPath);
console.log(JSON.stringify({locale:manifest.locale,uniqueRows:manifest.records.length,output:outputPath}));
