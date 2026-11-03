"""Generate native-only file checks and messages; never read user settings."""
import json
import sys
from pathlib import Path

payload, output, managed = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
messages = json.loads((Path(__file__).parent / 'host-messages.json').read_text(encoding='utf-8-sig'))
assert len(messages) == 20 and messages[0]['code'] == 'en'


def wide(text):
    return 'L' + json.dumps(text, ensure_ascii=False)


rows = []
for file in sorted(payload.rglob('*')):
    if not file.is_file():
        continue
    relative = file.relative_to(payload).as_posix()
    # Windows publish paths are case-insensitive; licenses are classified outside
    # Runtime by the packager after this manifest is generated.
    normalized = relative.casefold()
    if normalized in ('endfieldchargeplus.exe', 'license', 'privacy.md', 'notice.md') or normalized.startswith('licenses/'):
        continue
    rows.append('    {' + wide(relative.replace('/', '\\')) + f', {file.stat().st_size}ULL' + '},')
assert any('EndfieldChargePlus.dll' in row for row in rows)
text = '#pragma once\nstruct ExpectedFile { const wchar_t* path; uint64_t size; };\n'
text += 'static constexpr wchar_t managedAssemblyPath[] = ' + wide(managed) + ';\n'
text += 'static constexpr ExpectedFile expectedFiles[] = {\n' + '\n'.join(rows) + '\n};\n'
text += 'struct NativeMessages { const wchar_t* code; const wchar_t* title; const wchar_t* instructions; const wchar_t* location; const wchar_t* runtime; const wchar_t* details; };\n'
text += 'static constexpr NativeMessages nativeMessages[] = {\n'
for item in messages:
    values = [item[k] for k in ('code', 'title', 'instructions', 'location', 'runtime', 'details')]
    assert all(values) and 'Endfield Charge Plus' in item['title']
    text += '    {' + ', '.join(wide(value) for value in values) + '},\n'
text += '};\n'
(output / 'host-generated.h').write_text(text, encoding='utf-8')
print(f'Native host input: {len(rows)} runtime file checks; {len(messages)} embedded message languages.')
