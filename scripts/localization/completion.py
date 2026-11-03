"""Fail closed before distribution when any catalog language is unfinished.

The authored folder is a work area. Only the actual runtime files are eligible
for export. Structural coverage, English fallback, native manuals and a review
of the exact file hashes are separate requirements.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import sys

CHAPTERS = 'start display profiles variables hardware sources cycle summons triggers special media instances maintenance troubleshooting'.split()
NATIVE_CHAPTERS = 'hardware sources special media maintenance troubleshooting'.split()
SLOTS = re.compile(r'\$\{[^{}\n]*\}|\{[^{}\n]*\}|\b[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_<>-]+)+')
NUMERIC_SLOT = re.compile(r'(?<!\$)\{\d+(?:,[^{}:]*)?(?::[^{}]*)?\}')
BAD_TEXT = re.compile(r'\ufffd|<unk>|[\x00-\x08\x0b\x0c\x0e-\x1f]')


def read(path):
    def unique(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError(f'Duplicate JSON key in {path}: {key}')
            result[key] = value
        return result
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tokens(text):
    result=[]
    for match in SLOTS.finditer(text):
        token=match.group()
        # Mongolian and Tamil attach case endings to a cited identifier with a
        # hyphen. The ending is prose, not an edit of an executable {token}.
        if token.endswith('-') and match.end()<len(text) and ord(text[match.end()])>127 and text[match.end()].isalpha():
            token=token[:-1]
        if token!='e.g':result.append(token)
    return Counter(result)


def syntax_valid(key, value, code, requirements):
    if Counter(NUMERIC_SLOT.findall(key)) != Counter(NUMERIC_SLOT.findall(value)):
        return False
    if code in {'zh-CN', 'zh-TW', 'zh-HK'}:
        return True
    original_tokens = tokens(key)
    actual_tokens = tokens(value)
    # Generated English IDs can be shorter than the Chinese definition.
    # Faithful translations can also cite original API/variable paths.
    source_tokens = Counter(requirements.get('sourceTokens', {}).get(key, {}))
    allowed_tokens = original_tokens | source_tokens
    normalized=Counter()
    for token,count in actual_tokens.items():
        stem=next((known for known in sorted(allowed_tokens,key=len,reverse=True) if not token.startswith(('{','${')) and token.startswith(known+'-') and token[len(known)+1:].replace('-','').isalpha()),token)
        normalized[stem]+=count
    actual_tokens=normalized
    return not (original_tokens - actual_tokens) and not (actual_tokens - allowed_tokens)


def english_fallback(key, value, technical, shared, english_family):
    if english_family or key in technical or key in shared:
        return False
    # Capitalization or a trailing full stop does not translate a short label.
    # Keep hyphens/word boundaries intact: API-status can be idiomatic Dutch.
    if value.strip().rstrip('.').casefold() == key.strip().rstrip('.').casefold():
        return True
    # A non-English lookup ID may still have a prose explanation in its source;
    # only the explicit technical list permits an unchanged whole value.
    if not re.search(r'[A-Za-z]', SLOTS.sub('', key)):
        return False
    # A translated prefix followed by the original paragraph is still English.
    return len(re.findall(r'[A-Za-z]+', key)) >= 6 and key.strip().casefold() in value.casefold()


def valid_shared(entries):
    """Same-spelling native words are reviewed per locale, never whole sentences."""
    return {
        key: reason for key, reason in entries.items()
        if isinstance(key, str) and isinstance(reason, str) and len(reason.strip()) >= 12
        and 1 <= len(re.findall(r'[^\W\d_]+',NUMERIC_SLOT.sub('',key))) <= 3
        and len(NUMERIC_SLOT.sub('',key)) <= 32
        and not any(mark in NUMERIC_SLOT.sub('',key) for mark in '.!?;\n{}')
    }


def strings(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for key, child in value.items():
            if key not in {'id', 'target', 'code'}:
                yield from strings(child)
    elif isinstance(value, list):
        for child in value:
            yield from strings(child)


def manual_errors(path, android, english_sentences, english_family):
    errors = []
    try:
        document = read(path)
        if android:
            if not isinstance(document, list) or {c.get('id') for c in document} != set(CHAPTERS):
                return ['Expected all 14 Android manual chapters']
            if len(document)!=len(CHAPTERS):errors.append('Duplicate Android manual chapter')
            for chapter in document:
                if not chapter.get('title') or not chapter.get('summary') or not chapter.get('blocks'):
                    errors.append(f'Empty chapter: {chapter.get("id")}')
                ids=[block.get('id') for block in chapter.get('blocks',[])]
                if any(not value for value in ids) or len(ids)!=len(set(ids)):
                    errors.append(f'Duplicate or missing manual anchor: {chapter.get("id")}')
                for block in chapter.get('blocks', []):
                    if not block.get('title') or not block.get('body'):
                        errors.append(f'Empty manual block: {chapter.get("id")}/{block.get("id")}')
        else:
            if set(document) != set(CHAPTERS + ['_labels']) or len(document['_labels']) != 5:
                return ['Expected all 14 desktop manual chapters and 5 labels']
            for chapter in CHAPTERS:
                parts = document[chapter]
                if len(parts) != 4 or not all(isinstance(p, str) and p.strip() for p in parts):
                    errors.append(f'Empty chapter: {chapter}')
                elif len(parts[2].splitlines()) < 3:
                    errors.append(f'Missing manual steps: {chapter}')
        for value in strings(document):
            if BAD_TEXT.search(value):
                errors.append('Broken text in manual')
            if not english_family and len(re.findall(r'[A-Za-z]+', value)) >= 4:
                if value in english_sentences or any(sentence in value for sentence in english_sentences if len(sentence) > 70):
                    errors.append('English manual fallback: ' + value[:160])
    except (OSError, ValueError, TypeError, KeyError) as error:
        errors.append(str(error))
    return sorted(set(errors))


def platform_manual_errors(path, platform, english_path, english_family):
    """Check actual platform-authored prose, not an OS-name replacement."""
    errors=[]
    try:
        document=read(path)
        if len(document.get('_labels',[]))!=5 or not all(isinstance(value,str) and value.strip() for value in document['_labels']):
            return ['Expected five translated native manual labels']
        chapters=document[platform]
        expected=CHAPTERS if platform=='android' else NATIVE_CHAPTERS
        if set(chapters)!=set(expected):return ['Expected authored '+platform+' manual chapters']
        english=read(english_path)[platform]
        for chapter in expected:
            text=chapters[chapter]
            if not isinstance(text,list) or len(text)!=4 or not all(isinstance(value,str) and value.strip() for value in text):
                errors.append('Empty platform manual chapter: '+chapter);continue
            if len(text[2].splitlines())<3:errors.append('Missing native operating steps: '+chapter)
            for index,value in enumerate(text):
                original=english[chapter][index]
                if BAD_TEXT.search(value):errors.append('Broken native manual text: '+chapter)
                if not syntax_valid(original,value,'platform-manual',{}):errors.append('Changed native manual syntax: '+chapter+'/'+str(index))
                if not english_family and len(re.findall(r'[A-Za-z]+',original))>=6 and original.strip() in value:
                    errors.append('English native manual fallback: '+chapter+'/'+str(index))
    except (OSError,ValueError,TypeError,KeyError) as error:
        errors.append('Missing or invalid authored '+platform+' manual: '+str(error))
    return sorted(set(errors))


def resource_dir(repo):
    android = (repo / 'core/src/main/resources/localization').is_dir()
    return (repo / 'core/src/main/resources/localization' if android else repo / 'Localization'), android


def audit_repo(repo):
    directory, android = resource_dir(repo)
    requirements = read(directory / 'reviewed-release-requirements.json')
    reviews_path = directory / 'reviewed-language-completion.json'
    reviews = read(reviews_path) if reviews_path.is_file() else {}
    shared_path = directory / 'reviewed-shared-words.json'
    shared_all = read(shared_path) if shared_path.is_file() else {}
    english_family = set(requirements['englishFamily'])
    platform='android' if android else 'macos' if repo.name.endswith('-for-macos') else 'linux' if repo.name.endswith('-for-linux') else None
    technical = set(requirements['technicalStrings'])
    catalog = read(directory / ('catalog.json' if android else 'reviewed-language-catalog.json'))['languages']
    catalog_codes = [row['code'] for row in catalog]
    catalog_valid = len(catalog_codes) == len(set(catalog_codes)) and set(catalog_codes) == set(requirements['localeCodes'])
    canonical_path = directory / 'manual.full.en-US.json'
    english_sentences = {s for s in strings(read(canonical_path)) if len(re.findall(r'[A-Za-z]+', s)) >= 4} if canonical_path.is_file() else set(requirements['englishManualText'])
    result = []
    for code in requirements['localeCodes']:
        path = directory / (code + '.json')
        row = dict(code=code, missing=[], english=[], syntax=[], malformed=[], manual=[], review=[])
        try:
            pack = read(path)
            shared = valid_shared(shared_all.get(code, {}))
            for key in requirements['requiredKeys']:
                value = pack.get(key)
                if not isinstance(value, str) or not value.strip():
                    row['missing'].append(key)
                    continue
                if BAD_TEXT.search(value):
                    row['malformed'].append(key)
                if english_fallback(key, value, technical, shared, code in english_family):
                    row['english'].append(key)
                if not syntax_valid(key,value,code,requirements):
                    row['syntax'].append(key)
            guide = directory / (('manual.full.' if android else 'manual.') + code + '.json')
            embedded_guide = not android and (code in english_family or code in {'zh-CN', 'zh-TW', 'zh-HK'})
            if not embedded_guide:
                row['manual'] = manual_errors(guide, android, english_sentences, code in english_family)
            platform_guide=directory/('manual.platform.'+code+'.json')
            if platform:
                row['manual'].extend(platform_manual_errors(platform_guide,platform,directory/'manual.platform.en-US.json',code in english_family))
            review = reviews.get(code, {})
            if review.get('messagesSha256') != digest(path):
                row['review'].append('Exact message file has not passed semantic and visual review')
            if review.get('sourceSha256') != requirements['sourceSha256']:
                row['review'].append('Review is not for the current complete source catalog')
            if not review.get('checkedBy') or not review.get('crossCheckedBy') or review.get('checkedBy') == review.get('crossCheckedBy'):
                row['review'].append('Independent cross-check is required')
            if not embedded_guide and guide.is_file() and review.get('manualSha256') != digest(guide):
                row['review'].append('Exact manual file has not passed review')
            if platform and platform_guide.is_file() and review.get('platformManualSha256')!=digest(platform_guide):
                row['review'].append('Exact native platform instructions have not passed review')
            if platform and review.get('platformManualSourceSha256')!=requirements.get('platformManualSourceSha256','unavailable'):
                row['review'].append('Native manual review is not for the current platform source')
        except (OSError, ValueError, TypeError) as error:
            row['malformed'].append(str(error))
        row['complete'] = catalog_valid and not any(row[field] for field in ('missing', 'english', 'syntax', 'malformed', 'manual', 'review'))
        result.append(row)
    return dict(repository=repo.name, catalogValid=catalog_valid, locales=len(result), complete=sum(r['complete'] for r in result), languages=result)


def audit_workspace(workspace):
    names = ['zmd-charge-plus', 'zmd-charge-plus-for-linux', 'zmd-charge-plus-for-macos', 'zmd-charge-plus-for-android']
    reports = [audit_repo(workspace / name) for name in names]
    mismatch = []
    primary, _ = resource_dir(workspace / names[0])
    requirements = read(primary / 'reviewed-release-requirements.json')
    # The same message dictionaries are used on all platforms. Platform-specific
    # manual adaptations are reviewed separately rather than copied blindly.
    for code in requirements['localeCodes']:
        source = primary / (code + '.json')
        for name in names[1:]:
            target, _ = resource_dir(workspace / name)
            path = target / source.name
            if not source.is_file() or not path.is_file() or digest(source) != digest(path):
                mismatch.append(f'{name}/{source.name}')
    return dict(platforms=reports, crossPlatformMismatch=mismatch, ready=all(r['complete'] == r['locales'] for r in reports) and not mismatch)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    scope = parser.add_mutually_exclusive_group()
    scope.add_argument('--repo', type=Path, default=None)
    scope.add_argument('--workspace', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    try:
        if args.workspace:
            report = audit_workspace(args.workspace.resolve())
        else:
            result = audit_repo((args.repo or Path(__file__).resolve().parents[2]).resolve())
            report = dict(platforms=[result], crossPlatformMismatch=[], ready=result['complete'] == result['locales'])
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(dict(ready=report['ready'], platforms=[{k: p[k] for k in ('repository', 'locales', 'complete')} for p in report['platforms']], crossPlatformMismatch=len(report['crossPlatformMismatch'])), ensure_ascii=False))
        if not report['ready']:
            print('EXPORT BLOCKED: Finish all locale messages, native manuals and independent review before distribution.', file=sys.stderr)
            return 1
        return 0
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(f'EXPORT BLOCKED: Cannot establish translation completion: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
