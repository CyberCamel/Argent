#!/usr/bin/env python3
"""Second pass of the control migration.

migrate_controls.py only matched class strings that were *entirely* a button
definition. This one tolerates incidental extra utilities (mr-2, w-full, ms-1,
disabled:*, ...) before or after the definition, preserves them, and swaps the
button/input definition itself for the component class.

Usage:  migrate_controls_pass2.py [--check]
"""
import re
import sys
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]

# Utilities that are part of a hand-rolled control definition and are replaced
# by the component class.
DEF_TOKENS = {
    'px-2', 'px-3', 'px-4', 'py-1', 'py-1.5', 'py-2', 'py-2.5',
    'text-sm', 'text-xs', 'font-medium', 'text-white',
    'rounded-lg', 'rounded', 'rounded-md', 'shadow-sm', 'shadow',
    'transition-colors', 'inline-flex', 'items-center', 'gap-1', 'gap-2',
    'disabled:opacity-50', 'disabled:cursor-not-allowed', 'focus:outline-none',
    'w-full', 'border', 'outline-none', 'transition',
}

PRIMARY_DEFS = {
    'px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors shadow-sm',
    'px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors shadow-sm',
    'px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors',
    'px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors',
}

SECONDARY_DEFS = {
    'px-2 py-1 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-600 rounded-lg transition-colors',
    'px-2 py-1 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
    'px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-600 rounded-lg transition-colors',
    'px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
    'px-3 py-2 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
    'px-4 py-2 text-sm font-medium text-ink bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
    'px-3 py-1.5 text-sm font-medium text-ink-secondary bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
    'px-3 py-2 text-sm font-medium text-ink-secondary bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
    'px-2 py-1 text-sm font-medium text-ink-secondary bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
    'px-3 py-1 text-sm font-medium text-ink bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors',
}

INPUT_DEFS = {
    'w-full px-3 py-2 border border-line-strong rounded-lg bg-surface text-ink-heading text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 outline-none transition-colors',
    'w-full px-3 py-2 border border-gray-300 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-ink-heading text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 outline-none transition-colors',
    'w-full px-3 py-2 border border-gray-300 dark:border-gray-700 rounded-lg bg-surface text-ink text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 outline-none transition-colors',
}

ALL_DEFS = {}


def register(defs, comp):
    for d in defs:
        ALL_DEFS[d] = comp(d)


def primary_comp(d):
    return 'btn btn-primary' + (' btn-sm' if 'py-1.5' in d or 'py-1 ' in d else '')


def secondary_comp(d):
    size = ' btn-sm' if ('py-1' in d or 'py-1.5' in d) else ''
    return 'btn btn-secondary' + size


register(PRIMARY_DEFS, primary_comp)
register(SECONDARY_DEFS, secondary_comp)
for d in INPUT_DEFS:
    ALL_DEFS[d] = 'form-input'

CLASS_ATTR = re.compile(r'class="([^"]*)"')
TOK = re.compile(r'[a-z0-9-]+:[a-z0-9./-]+|[a-z0-9./\[\]%-]+')


def convert(attr):
    """Replace the longest matching definition found inside the token list."""
    # A Razor expression may legitimately repeat `?`, `:`, `""`, `==` and `is`.
    # Token-level rewriting is not safe there; leave it to a human.
    if '@(' in attr or '@@' in attr:
        return None

    best = None
    for d in ALL_DEFS:
        if d not in attr:
            continue
        # must appear as a contiguous, whitespace-delimited run
        if not re.search(r'(?:(?<=^)|(?<=[\s"]))' + re.escape(d) + r'(?=$|[\s"])', attr):
            continue
        if best is None or len(d) > len(best[0]):
            best = (d, ALL_DEFS[d])
    if best is None:
        return None
    d, comp = best
    rest = attr.replace(d, comp)
    toks = rest.split()
    # `w-full` is supplied by the component for inputs; drop it from buttons.
    if comp == 'form-input':
        toks = [t for t in toks if t != 'w-full']
    return ' '.join(toks)


def main():
    check = '--check' in sys.argv
    targets = []
    for pat in ('Argent.Web/Pages/**/*.cshtml', 'Argent.WebComponents/**/*.razor'):
        targets.extend(sorted(ROOT.glob(pat)))
    files = hits = 0
    for path in targets:
        if path.name == '_Layout.cshtml' or 'ModelerPropertiesPanel' in path.name:
            continue
        src = path.read_text(encoding='utf-8')
        out, pos, n = [], 0, 0
        for m in CLASS_ATTR.finditer(src):
            new = convert(m.group(1))
            if new is None or new == m.group(1):
                continue
            out.append(src[pos:m.start(1)])
            out.append(new)
            pos = m.end(1)
            n += 1
        if not n:
            continue
        out.append(src[pos:])
        path.write_text(''.join(out), encoding='utf-8') if not check else None
        files += 1
        hits += n
        print(f'{path.relative_to(ROOT)}: {n}')
    print(f'\n{hits} class attributes {"would change" if check else "changed"} across {files} files')


if __name__ == '__main__':
    main()
