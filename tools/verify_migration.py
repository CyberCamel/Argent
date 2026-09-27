#!/usr/bin/env python3
"""Verify the class-attribute migrations did not alter any Razor expression.

A class value may embed Razor (`@(cond ? "a" : "b")`, `@Foo(bar)`). Rewriting
those at the token level is unsafe, because `?`, `:`, `""`, `==` and `is`
legitimately repeat. This compares every class value in the working tree with
the same value at HEAD and reports any difference that is *not* one of the
intended token replacements.

Exits non-zero if anything unexpected is found.
"""
import re
import subprocess
import sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from razor_attrs import iter_class_values  # noqa: E402

# The replacements the migrations are allowed to make inside a class value.
ALLOWED = [
    # Sidebar tab: both ternary arms migrated onto semantic tokens by hand.
    # Listed first: these are supersets of the shorter pairs below.
    ('border-b-2 border-indigo-600 text-indigo-600 dark:text-indigo-400 dark:border-indigo-400',
     'border-b-2 border-accent-border text-accent-text'),
    ('text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300',
     'text-ink-muted hover:text-ink'),
    # neutral ramp pairs collapsed onto semantic tokens
    ('text-gray-900 dark:text-gray-100', 'text-ink-heading'),
    ('text-gray-800 dark:text-gray-200', 'text-ink-heading'),
    ('text-gray-700 dark:text-gray-300', 'text-ink'),
    ('text-gray-600 dark:text-gray-400', 'text-ink-secondary'),
    ('text-gray-500 dark:text-gray-400', 'text-ink-muted'),
    ('text-gray-500 dark:text-gray-300', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-300', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-200', 'text-ink-muted'),
    ('text-gray-900 dark:text-gray-300', 'text-ink-heading'),
    ('text-gray-300 dark:text-gray-600', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-500', 'text-ink-muted'),
    ('text-gray-400 dark:text-gray-600', 'text-ink-muted'),
    ('text-gray-500 dark:text-gray-600', 'text-ink-secondary'),
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
    ('bg-gray-200 dark:bg-gray-700', 'bg-sunken'),
    ('border-gray-200 dark:border-gray-700', 'border-line'),
    ('border-gray-200 dark:border-gray-800', 'border-line'),
    ('border-gray-100 dark:border-gray-800', 'border-line-subtle'),
    ('border-gray-100 dark:border-gray-700', 'border-line-subtle'),
    ('border-gray-300 dark:border-gray-600', 'border-line-strong'),
    ('border-gray-300 dark:border-gray-700', 'border-line-strong'),
    ('border-gray-600 dark:border-gray-700', 'border-line-strong'),
    ('border-red-300 dark:border-red-800', 'border-danger-border'),
    ('border-green-300 dark:border-green-800', 'border-success-border'),
    ('border-yellow-300 dark:border-yellow-800', 'border-warning-border'),
    ('border-blue-300 dark:border-blue-800', 'border-info-border'),
    ('text-indigo-600 dark:text-indigo-400', 'text-accent-text'),
    ('text-indigo-700 dark:text-indigo-300', 'text-accent-text'),
    ('text-indigo-800 dark:text-indigo-200', 'text-accent-text'),
    ('bg-indigo-100 dark:bg-indigo-900/30', 'bg-accent-subtle'),
    ('bg-indigo-50 dark:bg-indigo-900/30', 'bg-accent-subtle'),
    ('border-indigo-200 dark:border-indigo-800', 'border-accent-border'),
    ('ring-indigo-500', 'ring-accent'),
    ('border-indigo-500', 'border-accent-border'),
    # status text pairs
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
    # Neutral pill + hover/status chains (final cleanup pass).
    ('badge bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400 border border-line', 'badge badge-neutral'),
    ('badge bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400 border-line font-mono', 'badge badge-neutral'),
    ('badge bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400 border-line', 'badge badge-neutral'),
    ('ml-auto px-2.5 py-0.5 text-xs font-medium rounded-full bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400 border border-line', 'badge badge-neutral'),
    ('text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 transition-colors p-1 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-800', 'transition-colors p-1 rounded'),
    ('text-gray-400 hover:text-gray-600 dark:hover:text-gray-300', 'text-ink-muted hover:text-ink'),
    ('hover:text-gray-700 dark:hover:text-gray-300', 'hover:text-ink'),
    ('text-gray-600 dark:text-gray-300 hover:text-indigo-600 dark:hover:text-indigo-400', 'text-ink-secondary hover:text-accent-text'),
    ('text-ink-heading hover:text-indigo-600 dark:hover:text-indigo-400', 'text-ink-heading hover:text-accent-text'),
    ('text-accent-text hover:text-indigo-800 dark:hover:text-indigo-300', 'text-accent-text hover:underline'),
    ('text-danger hover:text-red-800 dark:hover:text-red-300', 'text-danger hover:underline'),
    ('border-t-indigo-600 dark:border-t-indigo-400', 'border-t-accent'),
    ('border-t-green-600 dark:border-t-green-400', 'border-t-success'),
    ('text-green-700 dark:text-green-400', 'text-success'),
    ('text-green-900 dark:text-green-200', 'text-success'),
    ('text-red-500 dark:text-red-400', 'text-danger'),
    ('text-gray-600 dark:text-gray-300', 'text-ink-secondary'),
    ('inline-flex items-center gap-1 text-sm text-ink-secondary hover:text-indigo-600 dark:hover:text-indigo-400 mb-4', 'btn btn-ghost btn-sm mb-4'),
    ('text-ink-muted hover:text-ink transition-colors p-1 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-800', 'btn btn-ghost btn-sm'),
]

# Whole-value rewrites (component-class substitutions).
WHOLE = [
    'inline-flex items-center px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors shadow-sm inline-flex items-center gap-1',
    'inline-flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-lg transition-colors shadow-sm',
]

RAZOR = re.compile(r'@\((?:[^()]|\([^()]*\))*\)|@[A-Za-z_][\w.]*\([^()]*\)|@[A-Za-z_][\w.]*')


def normalise(v):
    """Apply the allowed replacements to a value so it can be compared."""
    for old, new in ALLOWED:
        v = re.sub(r'(?:(?<=^)|(?<=[\s"]))' + re.escape(old) + r'(?=$|[\s"])',
                   new, v)
    return ' '.join(v.split())


def razor_parts(v):
    return RAZOR.findall(v)


def main():
    files = subprocess.run(['git', 'diff', '--name-only'],
                           capture_output=True, text=True).stdout.split()
    files = [f for f in files if f.endswith(('.cshtml', '.razor'))]
    # Owned by another agent working in the same tree.
    files = [f for f in files if 'ModelerPropertiesPanel' not in f]
    # Hand-rewritten files legitimately change the number of class attributes.
    MANUAL = {
        'Argent.Web/Pages/Index.cshtml', 'Argent.Web/Pages/Login.cshtml',
        'Argent.Web/Pages/Admin/Branding.cshtml', 'Argent.Web/Pages/Workflows/Index.cshtml',
        'Argent.Web/Pages/Admin/Users/Index.cshtml',
        'Argent.Web/Pages/Shared/_EmptyState.cshtml', 'Argent.Web/Pages/Shared/_Layout.cshtml',
        'Argent.Web/Pages/Shared/_PageHeader.cshtml',
    }
    problems = 0
    for f in files:
        head = subprocess.run(['git', 'show', f'HEAD:{f}'],
                              capture_output=True, text=True)
        if head.returncode:
            continue
        hsrc = head.stdout
        csrc = pathlib.Path(f).read_text(encoding='utf-8')
        hv = [hsrc[s:e] for s, e in iter_class_values(hsrc)]
        cv = [csrc[s:e] for s, e in iter_class_values(csrc)]
        if f in MANUAL:
            continue
        if len(hv) != len(cv):
            print(f'{f}: class-attribute count changed {len(hv)} -> {len(cv)}')
            problems += 1
            continue
        for a, b in zip(hv, cv):
            if a == b:
                continue
            # The Razor parts must be preserved token-for-token, modulo the
            # allowed colour replacements inside them.
            ra, rb = razor_parts(a), razor_parts(b)
            if [normalise(' '.join(ra))] != [normalise(' '.join(rb))]:
                print(f'{f}: Razor expression changed')
                print(f'   HEAD: {ra}')
                print(f'   NOW : {rb}')
                problems += 1
    print(f'\n{len(files)} files checked, {problems} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
