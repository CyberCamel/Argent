#!/usr/bin/env python3
"""Replace the hand-written button and input utility strings with the shared
component primitives.

The application grew its own copy of "what a primary button looks like" as a
literal Tailwind class string, repeated with small variations (px-3 vs px-4,
with and without shadow-sm, with and without disabled: utilities). Every one of
those is a slightly different button. This maps the whole family onto
.btn / .btn-primary / .btn-secondary / .form-input / .form-textarea.

Usage:  migrate_controls.py [--check] [--apply <file> ...]
"""
import re
import sys
import pathlib
import sys as _sys
_sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from razor_attrs import map_values as _map_values

ROOT = pathlib.Path(__file__).resolve().parents[1]

# Longest / most specific first.
EXACT = {
    # --- primary buttons -------------------------------------------------
    'px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors shadow-sm inline-flex items-center gap-1':
        'btn btn-primary',
    'px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors shadow-sm inline-flex items-center gap-1':
        'btn btn-primary btn-sm',
    'px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed':
        'btn btn-primary btn-sm',
    'px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors':
        'btn btn-primary btn-sm',
    'px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors shadow-sm':
        'btn btn-primary',

    # --- secondary / ghost buttons --------------------------------------
    'px-2 py-1 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-600 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed':
        'btn btn-secondary btn-sm',
    'px-2 py-1 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-600 rounded-lg transition-colors mr-2':
        'btn btn-secondary btn-sm me-2',
    'px-2 py-1 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-600 rounded-lg transition-colors mr-1':
        'btn btn-secondary btn-sm me-1',
    'px-2 py-1 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors':
        'btn btn-secondary btn-sm',
    'px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors':
        'btn btn-secondary btn-sm',
    'px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-600 rounded-lg transition-colors':
        'btn btn-secondary btn-sm',
    'px-3 py-2 text-sm font-medium text-gray-600 dark:text-gray-300 bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors':
        'btn btn-secondary',
    'px-4 py-2 text-sm font-medium text-ink bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors':
        'btn btn-secondary',
    'px-3 py-1.5 text-sm font-medium text-ink-secondary bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors':
        'btn btn-secondary btn-sm',
    'px-3 py-1.5 text-sm font-medium text-ink-secondary bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors disabled:opacity-50':
        'btn btn-secondary btn-sm',
    'px-3 py-2 text-sm font-medium text-ink-secondary bg-sunken hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors':
        'btn btn-secondary',

    # --- inputs ----------------------------------------------------------
    'w-full px-3 py-2 border border-line-strong rounded-lg bg-surface text-ink-heading text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 outline-none transition-colors':
        'form-input',

    # --- close buttons in modals ----------------------------------------
    'text-gray-400 hover:text-gray-600 dark:hover:text-gray-300': 'btn btn-ghost',
}

# Regex fallbacks for the same shapes with incidental extra utilities.
REGEX = [
    # primary: any indigo-600 filled button
    (re.compile(r'^(?:inline-flex items-center gap-1\s+)?px-(\d(?:\.\d)?) py-(\d(?:\.\d)?) '
                r'text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 '
                r'rounded-lg transition-colors(?: shadow-sm)?'
                r'(?: disabled:opacity-50 disabled:cursor-not-allowed)?$'),
     lambda m: 'btn btn-primary' + (' btn-sm' if m.group(1) == '3' else '')),
    # secondary: any sunken button with gray text
    (re.compile(r'^px-(\d(?:\.\d)?) py-(\d(?:\.\d)?) text-sm font-medium '
                r'text-(?:gray-600 dark:text-gray-300|ink|ink-secondary) '
                r'bg-sunken hover:bg-gray-200 dark:hover:bg-gray-\d+ '
                r'rounded-lg transition-colors'
                r'(?: disabled:opacity-50(?: disabled:cursor-not-allowed)?)?$'),
     lambda m: 'btn btn-secondary' + (' btn-sm' if m.group(1) in ('2', '3') else '')),
    # text input / textarea
    (re.compile(r'^w-full px-3 py-2 border border-line-strong rounded-lg bg-surface '
                r'text-ink-heading text-sm focus:ring-2 focus:ring-indigo-500 '
                r'focus:border-indigo-500 outline-none transition-colors$'),
     lambda m: 'form-input'),
]

CLASS_ATTR = re.compile(r'class="([^"]*)"')


def convert(attr):
    # A Razor expression may legitimately repeat `?`, `:`, `""`, `==` and `is`.
    # Token-level rewriting is not safe there; leave it to a human.
    if '@(' in attr or '@@' in attr:
        return None
    if attr in EXACT:
        return EXACT[attr]
    for rx, fn in REGEX:
        m = rx.match(attr)
        if m:
            return fn(m)
    return None


def process(src):
    return _map_values(src, convert)


def main():
    args = sys.argv[1:]
    check = '--check' in args
    explicit = args[args.index('--apply') + 1:] if '--apply' in args else []
    if explicit:
        targets = [pathlib.Path(a) for a in explicit]
    else:
        targets = []
        for pat in ('Argent.Web/Pages/**/*.cshtml', 'Argent.WebComponents/**/*.razor'):
            targets.extend(sorted(ROOT.glob(pat)))

    files = hits = 0
    for path in targets:
        if path.name == '_Layout.cshtml' or 'ModelerPropertiesPanel' in path.name:
            continue
        src = path.read_text(encoding='utf-8')
        new_src, n = process(src)
        if not n:
            continue
        files += 1
        hits += n
        print(f'{path.relative_to(ROOT)}: {n}')
        if not check:
            path.write_text(new_src, encoding='utf-8')
    verb = 'would change' if check else 'changed'
    print(f'\n{hits} class attributes {verb} across {files} files')


if __name__ == '__main__':
    main()
