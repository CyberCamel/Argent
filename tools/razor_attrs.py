#!/usr/bin/env python3
"""Locate Razor `class="..."` attribute values correctly.

A naive `class="([^"]*)"` regex truncates at the first inner double quote, which
breaks on any attribute containing a Razor expression with a string literal,
e.g.

    class="cve-opt @(_mode == Mode.Any ? "active" : "")"

This scanner tracks Razor expression depth so the value is captured in full.
"""
import re

ATTR_START = re.compile(r'\bclass="')


def iter_class_values(src):
    """Yield (value_start, value_end) offsets for each class attribute value.

    The value ends at a double quote that is at Razor depth 0 and is not
    escaped by a preceding '@' (Razor's `@@` escape for a literal '@').
    """
    pos = 0
    while True:
        m = ATTR_START.search(src, pos)
        if m is None:
            return
        i = m.end()
        depth = 0
        start = i
        while i < len(src):
            c = src[i]
            if c == '@' and i + 1 < len(src) and src[i + 1] == '(':
                depth += 1
                i += 2
                continue
            if c == '@' and i + 1 < len(src) and src[i + 1] == '@':
                i += 2          # escaped '@', not an expression
                continue
            if c == '(' and depth > 0:
                depth += 1
            elif c == ')' and depth > 0:
                depth -= 1
            elif c == '"' and depth == 0:
                break
            i += 1
        yield start, i
        pos = i + 1


def map_values(src, fn):
    """Rewrite every class attribute value through fn(value) -> new value."""
    out = []
    last = 0
    n = 0
    for start, end in iter_class_values(src):
        new = fn(src[start:end])
        if new is None or new == src[start:end]:
            continue
        out.append(src[last:start])
        out.append(new)
        last = end
        n += 1
    if not n:
        return src, 0
    out.append(src[last:])
    return ''.join(out), n
