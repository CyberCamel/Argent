#!/usr/bin/env python3
"""Migrate ad-hoc Tailwind status-colour combinations onto the shared component
primitives defined in Argent.Web/Styles/app.css.

The application had three recurring hand-rolled status patterns:

  * alert divs   -> bg-red-50 border-red-200 text-red-800 dark:bg-red-900/30 ...
  * status pills -> inline-flex ... rounded-full bg-amber-100 text-amber-700 ...
  * soft buttons -> px-3 py-1.5 text-green-600 bg-green-50 hover:bg-green-100 ...

Each encoded colour, tint, border and dark-mode override separately, which is
why those surfaces drifted apart and why `dark:` overrides had to be repeated
in every one of them. This maps them onto .alert-*, .badge-* and
.btn-*-secondary so the status palette is defined once.

Usage:  migrate_status_classes.py [--check] [--apply <file> ...]
"""
import re
import sys
import pathlib
import sys as _sys
_sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from razor_attrs import map_values as _map_values

ROOT = pathlib.Path(__file__).resolve().parents[1]

FAMILY = {
    'red': 'danger', 'rose': 'danger', 'crimson': 'danger',
    'green': 'success', 'emerald': 'success', 'teal': 'success',
    'yellow': 'warning', 'amber': 'warning', 'orange': 'warning',
    'blue': 'info', 'sky': 'info', 'cyan': 'info',
    'indigo': 'accent',
}

# Variant prefixes whose colour utilities must also be removed, otherwise they
# would keep overriding the component class in dark mode.
VARIANT_PREFIX = re.compile(
    r'^(?:dark|hover|focus|focus-visible|active|visited|group-hover|peer-hover|'
    r'sm|md|lg|xl|2xl|motion-safe|motion-reduce|print):')

COLOR_FAMILY = r'(red|rose|crimson|green|emerald|teal|yellow|amber|orange|blue|sky|indigo|cyan)'

# Any utility that paints in one of the status families, with or without a
# variant prefix and with or without an alpha suffix.
STATUS_UTIL = re.compile(
    r'^(?:[a-z0-9-]+:)*'
    r'(?:bg|text|border|ring|ring-offset|from|to|via|divide|placeholder|'
    r'decoration|outline|shadow|fill|stroke)-'
    + COLOR_FAMILY + r'-\d{2,3}(?:/\d{1,3})?$')

# Anchors used to recognise each pattern.
LIGHT_BG = re.compile(r'^(?:[a-z0-9-]+:)*bg-([a-z]+)-(50|100)$')
MID_BORDER = re.compile(r'^(?:[a-z0-9-]+:)*border-([a-z]+)-(200|300|400)$')
DARK_TEXT = re.compile(r'^(?:[a-z0-9-]+:)*text-([a-z]+)-(600|700|800)$')
HOVER_BG = re.compile(r'^hover:bg-([a-z]+)-(100|200)$')
HOVER_BG_DARK = re.compile(r'^dark:hover:bg-([a-z]+)-900/50$')

# The element carrying the class attribute; captured so a <span> is never
# turned into a block-level .alert.
OPEN_TAG = re.compile(r'<(span|div|p|li|td|th|section|button|a|form|label)\b([^<>]*)$')
CLASS_ATTR = re.compile(r'class="([^"]*)"')

# Values the component class supplies itself.
DROPPED = {
    'rounded', 'rounded-sm', 'rounded-md', 'rounded-lg', 'rounded-xl',
    'rounded-full', 'rounded-l-lg', 'rounded-r-lg', 'rounded-t', 'rounded-b',
    'shadow', 'shadow-sm', 'shadow-md', 'border', 'border-b',
    'inline-flex', 'flex', 'items-center', 'font-medium', 'uppercase',
    'tracking-wide', 'whitespace-nowrap', 'transition-colors',
    'px-3', 'py-1.5', 'text-sm',
}

INLINE_TAGS = {'span', 'a', 'label'}


def status_kind(tokens):
    fams = {m.group(1) for t in tokens
            for m in [LIGHT_BG.match(t)] if m and m.group(1) in FAMILY}
    if len(fams) != 1:
        return None, None
    fam = fams.pop()
    return FAMILY[fam], fam


def has(tokens, rx):
    return any(rx.match(t) for t in tokens)


def classify(tokens, tag, has_badge_class):
    """Return the replacement class string, or None to leave untouched."""
    # A Razor expression may legitimately repeat `?`, `:`, `""`, `==` and `is`;
    # token-level rewriting is unsafe there.
    if any('@(' in t or '@@' in t for t in tokens):
        return None
    kind, fam = status_kind(tokens)
    if kind is None:
        return None

    rest = [t for t in tokens if not STATUS_UTIL.match(t) and t not in DROPPED]

    # Soft (tinted) button.
    if 'transition-colors' in tokens and has(tokens, HOVER_BG) and has(tokens, HOVER_BG_DARK):
        keep = [t for t in rest if t not in ('w-full', 'w-auto')]
        return ' '.join(['btn', f'btn-{kind}-secondary', 'btn-sm'] + keep)

    # A status pill: rounded, small text, no block padding.
    is_pill = (has_badge_class
               or 'badge' in tokens
               or 'rounded-full' in tokens
               or tag in INLINE_TAGS
               or (has(tokens, DARK_TEXT)
                   and not any(t in ('p-3', 'p-4', 'px-4', 'py-2') for t in tokens)
                   and not has(tokens, MID_BORDER)))
    if is_pill:
        return ' '.join(['badge', f'badge-{kind}'] + [t for t in rest if t != 'badge'])

    # A block-level callout. .alert supplies its own padding.
    return ' '.join(['alert', f'alert-{kind}']
                    + [t for t in rest if t not in ('p-2', 'p-3', 'p-4')])


def process(src, only=None):
    def convert_value(v):
        head = src[max(0, src.index(v) - 200):src.index(v)]
        tag_m = OPEN_TAG.search(head)
        tag = tag_m.group(1) if tag_m else 'div'
        return classify(v.split(), tag, False)

    new_src, n = _map_values(src, convert_value)
    return new_src, n


def main():
    args = sys.argv[1:]
    check = '--check' in args
    explicit = [a for a in args[1:] if not a.startswith('--')] if '--apply' in args else []

    if explicit:
        targets = [pathlib.Path(a) for a in explicit]
    else:
        targets = []
        for pat in ('Argent.Web/Pages/**/*.cshtml', 'Argent.WebComponents/**/*.razor'):
            targets.extend(sorted(ROOT.glob(pat)))

    total_files = total_hits = 0
    for path in targets:
        if path.name == '_Layout.cshtml':
            continue
        if 'ModelerPropertiesPanel' in path.name:
            print(f'skip (owned by another agent): {path.name}')
            continue
        src = path.read_text(encoding='utf-8')
        new_src, hits = process(src)
        if not hits:
            continue
        total_files += 1
        total_hits += hits
        print(f'{path.relative_to(ROOT)}: {hits}')
        if not check:
            path.write_text(new_src, encoding='utf-8')

    verb = 'would change' if check else 'changed'
    print(f'\n{total_hits} class attributes {verb} across {total_files} files')


if __name__ == '__main__':
    main()
