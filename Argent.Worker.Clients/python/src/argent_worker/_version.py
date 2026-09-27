"""The single source of the client version.

Kept separate from the package initialiser so modules can read it without importing the package,
and so `pyproject.toml` can read the same value rather than repeating it.
"""

__version__ = "0.1.0"
