#!/usr/bin/env python3
"""Third pass: status text colours and pill-shaped elements.

migrate_status_classes.py only recognised status surfaces (an element that set a
background tint as well). Plenty of markup just colours *text* to signal status
("Claim", "Release", "Draft", validation hints), and plenty of pills were built
by hand with a utility string instead of .badge. Both are mapped here.

Usage:  migrate_status_text.py [--check]
"""
import re
import sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from razor_attrs import map_values  # noqa: E402

# (light, dark) -> semantic token. Order matters: longest / most specific first.
STATUS_PAIRS = [
    ('text-red-800 dark:text-red-200', 'text-danger'),
    ('text-red-700 dark:text-red-300', 'text-danger'),
    ('text-red-600 dark:text-red-400', 'text-danger'),
    ('text-red-500 dark:text-red-300', 'text-danger'),
    ('text-green-800 dark:text-green-200', 'text-success'),
    ('text-green-800 dark:text-green-300', 'text-success'),
    ('text-green-700 dark:text-green-300', 'text-success'),
    ('text-green-600 dark:text-green-400', 'text-success'),
    ('text-emerald-700 dark:text-emerald-300', 'text-success'),
    ('text-yellow-800 dark:text-yellow-200', 'text-warning'),
    ('text-yellow-700 dark:text-yellow-400', 'text-warning'),
    ('text-yellow-600 dark:text-yellow-400', 'text-warning'),
    ('text-amber-800 dark:text-amber-200', 'text-warning'),
    ('text-amber-700 dark:text-amber-400', 'text-warning'),
    ('text-blue-800 dark:text-blue-200', 'text-info'),
    ('text-blue-700 dark:text-blue-300', 'text-info'),
    ('text-blue-600 dark:text-blue-400', 'text-info'),
    ('text-sky-700 dark:text-sky-300', 'text-info'),

    # Remaining neutral pairs not covered by migrate_theme_pairs.py.
    ('text-gray-300 dark:text-gray-600', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-500', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-600', 'text-ink-muted'),
    ('text-gray-500 dark:text-gray-600', 'text-ink-secondary'),
    ('bg-gray-200 dark:bg-gray-700', 'bg-sunken'),
    ('border-red-300 dark:border-red-800', 'border-danger-border'),
    ('border-green-300 dark:border-green-800', 'border-success-border'),
    ('border-yellow-300 dark:border-yellow-800', 'border-warning-border'),
    ('border-blue-300 dark:border-blue-800', 'border-info-border'),
]

# Hand-rolled pills: the definition below is replaced by .badge badge-<kind>.
PILL_DEFS = {
    'inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-full bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400 border border-line': 'badge badge-neutral',
    'inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-full bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400': 'badge badge-neutral',
    'inline-flex items-center px-1.5 py-0.5 text-xs font-medium rounded-full bg-gray-200 text-gray-600 dark:bg-gray-700 dark:text-gray-400': 'badge badge-neutral',
    'px-1.5 py-0.5 rounded bg-gray-200 dark:bg-gray-700 text-xs text-gray-600 dark:text-gray-300': 'badge badge-neutral',
    'px-2 py-0.5 rounded text-xs font-medium bg-gray-100 dark:bg-gray-800 text-gray-700 dark:text-gray-300': 'badge badge-neutral',
}

DARK_ONLY = re.compile(r'^(?:dark:)?(?:bg|text|border|ring)-(gray|red|green|yellow|amber|blue|emerald|sky|indigo|slate|zinc|neutral|stone)-[0-9]{2,3}(?:/[0-9]{1,3})?$')


def convert(value):
    # Never rewrite a value containing a Razor expression. Token-level
    # de-duplication is unsafe there: `?`, `:`, `""`, `==` and `is` legitimately
    # repeat, and collapsing "duplicates" silently changes the expression.
    if '@(' in value or '@@' in value:
        return value

    toks = value.split()

    # 1. Longest matching pill definition wins.
    best = None
    for d, comp in PILL_DEFS.items():
        if d in value and (best is None or len(d) > len(best[0])):
            best = (d, comp)
    if best is not None:
        d, comp = best
        rest = [t for t in toks if t not in d.split()]
        seen, out_toks = set(), [comp]
        for t in rest:
            if t not in seen:
                seen.add(t)
                out_toks.append(t)
        return ' '.join(out_toks)

    # 2. Status / neutral pairs. The replacements are 1:1 and contiguous, so
    #    no token de-duplication is needed.
    joined = ' '.join(toks)
    for old, new in STATUS_PAIRS:
        pattern = r'(?:(?<=^)|(?<=[\s"]))' + re.escape(old) + r'(?=$|[\s"]|_)'
        if re.search(pattern, joined):
            joined = re.sub(pattern, new, joined)
    return joined


def main():
    check = '--check' in sys.argv
    targets = []
    for pat in ('Argent.Web/Pages/**/*.cshtml', 'Argent.WebComponents/**/*.razor'):
        targets.extend(sorted(pathlib.Path('.').glob(pat)))
    files = hits = 0
    for p in targets:
        if p.name == '_Layout.cshtml' or 'ModelerPropertiesPanel' in p.name:
            continue
        src = p.read_text(encoding='utf-8')
        out, n = map_values(src, convert)
        if not n:
            continue
        files += 1
        hits += n
        print(f'{p}: {n}')
        if not check:
            p.write_text(out, encoding='utf-8')
    print(f'\n{hits} class attributes {"would change" if check else "changed"} across {files} files')


if __name__ == '__main__':
    main()
