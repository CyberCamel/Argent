import sys
from pathlib import Path

# Run against the source tree so the suite needs no install step.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
