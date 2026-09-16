"""Lightweight source guards (not a C# compiler). Run from any working directory."""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# Consume literals before comments so URLs / comment-like text in strings are safe.
TOKENS = re.compile(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/')
EMPTY_HANDLER = re.compile(
    r'\bWith(?:Print|Save|New|Edit|Delete|Search|Refresh|Approve|Unapprove|Excel|Exit|List)'
    r'\(\s*\(\s*_\s*,\s*_\s*\)\s*=>\s*\{\s*\}')
MEMBER = re.compile(r'\bclass\s+(\w+)|\bpublic\s+double\??\s+(\w+)\s*(?:\{|=>)')


def code_only(text):
    """Blank literals/comments, preserving offsets and line numbers for diagnostics."""
    return TOKENS.sub(lambda m: re.sub(r'[^\n]', ' ', m.group()), text)


def empty_handlers(text):
    return [text.count('\n', 0, m.start()) + 1 for m in EMPTY_HANDLER.finditer(code_only(text))]


def double_members(path, text):
    current_class = ''
    members = set()
    for m in MEMBER.finditer(code_only(text)):
        if m[1]:
            current_class = m[1]
        else:
            members.add(f'{path}:{current_class}.{m[2]}')
    return members


def inspect(root):
    problems = []
    for p in (root / 'src').rglob('*.cs'):
        if {'bin', 'obj'} & set(p.parts):
            continue
        problems.extend(f'{p.relative_to(root)}:{line}: empty toolbar handler'
                        for line in empty_handlers(p.read_text(encoding='utf-8-sig')))

    for p in (root / 'src/DatesErp.Desktop').rglob('*.xaml'):
        if {'bin', 'obj', 'Themes'} & set(p.parts) or p.name == 'App.xaml':
            continue
        element = ET.parse(p).getroot()  # Invalid XML is itself a failing check.
        if element.attrib.get('FlowDirection') != 'RightToLeft':
            problems.append(f'{p.relative_to(root)}: root must declare RightToLeft')

    baseline = json.loads((root / 'tools/ci/legacy-double-members.json').read_text(encoding='utf-8'))
    actual = set()
    for p in (root / 'src/DatesErp.Core/Domain/Entities').glob('*.cs'):
        actual.update(double_members(p.name, p.read_text(encoding='utf-8-sig')))
    for member in sorted(actual - set(baseline['members'])):
        problems.append(f'New double member: {member}. Use decimal or obtain an explicit compatibility review.')
    print(f'Legacy double members: {len(actual)}; no new identities allowed (not just a count ceiling).')
    return problems


if __name__ == '__main__':
    findings = inspect(ROOT)
    for finding in findings:
        print(f'ERROR: {finding}', file=sys.stderr)
    print('Structural checks: ' + ('FAILED' if findings else 'PASS'))
    sys.exit(bool(findings))
