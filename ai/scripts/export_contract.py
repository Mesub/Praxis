"""Write the AI plane's OpenAPI schema to ``contracts/ai-plane.openapi.json``.

The contract is **committed**, not generated at build time by whoever happens to be running the .NET
build. That matters: the C# side must be able to check itself against the contract without booting a
Python service, and a reviewer must be able to see a wire-format change *in the diff* rather than
discovering it when a learner's grade comes back as null.

Run:  uv run python scripts/export_contract.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from praxis_ai.app import app

CONTRACT = Path(__file__).resolve().parents[2] / "contracts" / "ai-plane.openapi.json"


def main() -> None:
    CONTRACT.parent.mkdir(parents=True, exist_ok=True)

    CONTRACT.write_text(
        json.dumps(app.openapi(), indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )

    print(f"wrote {CONTRACT}")


if __name__ == "__main__":
    main()
