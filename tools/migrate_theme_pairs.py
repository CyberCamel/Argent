#!/usr/bin/env python3
"""Collapse light/dark utility pairs onto single semantic token utilities.

Because the neutral ramp in Styles/app.css is theme-relative (step N means
"N steps from the page background"), utilities like `bg-surface` or
`text-ink-muted` already follow the active theme. The hand-written
`bg-white dark:bg-gray-900` pairs throughout the markup therefore carry a
`dark:` variant that no longer does anything except add noise and make the
codebase harder to read.

This rewrites those pairs to the single token utility. The pair table is
exhaustive for the combinations actually present in the repository; anything
unrecognised is left untouched.

Usage:  migrate_theme_pairs.py [--check] [--apply <file> ...]
"""
import re
import sys
import pathlib
import sys as _sys
_sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from razor_attrs import map_values as _map_values

ROOT = pathlib.Path(__file__).resolve().parents[1]

# Ordered longest-first so multi-token pairs match before single tokens.
PAIRS = [
    # --- text ------------------------------------------------------------
    ('text-gray-900 dark:text-gray-100', 'text-ink-heading'),
    ('text-gray-800 dark:text-gray-200', 'text-ink-heading'),
    ('text-gray-700 dark:text-gray-300', 'text-ink'),
    ('text-gray-600 dark:text-gray-400', 'text-ink-secondary'),
    ('text-gray-500 dark:text-gray-400', 'text-ink-muted'),
    ('text-gray-500 dark:text-gray-300', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-300', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-200', 'text-ink-muted'),
    ('text-gray-900 dark:text-gray-300', 'text-ink-heading'),

    # --- surfaces --------------------------------------------------------
    ('bg-white dark:bg-gray-900', 'bg-surface'),
    ('bg-white dark:bg-gray-800', 'bg-surface'),
    ('bg-white dark:bg-gray-950', 'bg-surface'),
    ('bg-gray-50 dark:bg-gray-800', 'bg-sunken'),
    ('bg-gray-50 dark:bg-gray-900', 'bg-sunken'),
    ('bg-gray-50 dark:bg-gray-800/50', 'bg-sunken'),
    ('bg-gray-100 dark:bg-gray-800', 'bg-sunken'),
    ('bg-gray-100 dark:bg-gray-700', 'bg-sunken'),
    ('bg-gray-100 dark:bg-gray-900', 'bg-sunken'),
    ('bg-gray-900 dark:bg-gray-950', 'bg-sunken'),

    # --- borders ---------------------------------------------------------
    ('border-gray-200 dark:border-gray-700', 'border-line'),
    ('border-gray-200 dark:border-gray-800', 'border-line'),
    ('border-gray-100 dark:border-gray-800', 'border-line-subtle'),
    ('border-gray-100 dark:border-gray-700', 'border-line-subtle'),
    ('border-gray-300 dark:border-gray-600', 'border-line-strong'),
    ('border-gray-300 dark:border-gray-700', 'border-line-strong'),
    ('border-gray-600 dark:border-gray-700', 'border-line-strong'),

    # --- accent ----------------------------------------------------------
    ('text-indigo-600 dark:text-indigo-400', 'text-accent-text'),
    ('text-indigo-700 dark:text-indigo-300', 'text-accent-text'),
    ('text-indigo-800 dark:text-indigo-200', 'text-accent-text'),
    ('bg-indigo-100 dark:bg-indigo-900/30', 'bg-accent-subtle'),
    ('bg-indigo-50 dark:bg-indigo-900/30', 'bg-accent-subtle'),
    ('border-indigo-200 dark:border-indigo-800', 'border-accent-border'),
    ('ring-indigo-500', 'ring-accent'),
    ('border-indigo-500', 'border-accent-border'),

    # --- dividers inside lists / cards -----------------------------------
    ('border-gray-100 dark:border-gray-800', 'border-line-subtle'),
    ('border-b border-gray-100 dark:border-gray-800', 'border-b border-line-subtle'),
]

CLASS_ATTR = re.compile(r'class="([^"]*)"')


def convert(attr):
    # A Razor expression may legitimately repeat `?`, `:`, `""`, `==` and `is`.
    # Token-level rewriting is not safe there; leave it to a human.
    if '@(' in attr or '@@' in attr:
        return None
    toks = attr.split()
    out, i = [], 0
    changed = False
    while i < len(toks):
        # Try to match the longest pair starting at position i.
        matched = None
        for width in (3, 2):
            if i + width > len(toks):
                continue
            candidate = ' '.join(toks[i:i + width])
            for old, new in PAIRS:
                if candidate == old:
                    matched = (width, new)
                    break
            if matched:
                break
        if matched:
            width, new = matched
            out.append(new)
            i += width
            changed = True
        else:
            out.append(toks[i])
            i += 1
    if not changed:
        return None
    # De-duplicate while preserving order.
    seen, final = set(), []
    for t in out:
        if t not in seen:
            seen.add(t)
            final.append(t)
    return ' '.join(final)


def process(src):
    new_src, n = _map_values(src, convert)
    return new_src, n


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
        if path.name == '_Layout.cshtml':
            continue
        if 'ModelerPropertiesPanel' in path.name:
            print(f'skip (owned by another agent): {path.name}')
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

    verb = 'would rewrite' if check else 'rewrote'
    print(f'\n{hits} class attributes {verb} across {files} files')


if __name__ == '__main__':
    main()
