#!/usr/bin/env python3
"""
Step 2: Convert basic JSONL files (from step1) to SQLite databases.

The resulting .db files can be placed in ~/.cache/yottacast/dictionary/
so Yottacast uses them for local lookups instead of the Wiktionary API.

Usage:
  python step2_json_to_sqlite.py              # convert all languages in output/
  python step2_json_to_sqlite.py --lang es    # convert only Spanish
  python step2_json_to_sqlite.py --lang es de # convert Spanish and German

Input:  output/{lang_code}.jsonl
Output: output/{lang_code}.db
"""

import argparse
import json
import os
import sqlite3
import sys
import time
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent
OUTPUT_DIR = SCRIPT_DIR / "output"

SCHEMA = """
CREATE TABLE entries (
    word TEXT NOT NULL COLLATE NOCASE,
    pos  TEXT NOT NULL,
    definitions TEXT NOT NULL,
    example TEXT
);
CREATE INDEX idx_word ON entries(word COLLATE NOCASE);
"""

BATCH_SIZE = 1000


def convert(lang_code: str) -> None:
    jsonl_path = OUTPUT_DIR / f"{lang_code}.jsonl"
    db_path = OUTPUT_DIR / f"{lang_code}.db"
    tmp_path = OUTPUT_DIR / f"{lang_code}.db.tmp"

    if not jsonl_path.exists():
        print(f"[{lang_code}] {jsonl_path.name} not found — run step1 first", file=sys.stderr)
        return

    print(f"\n[{lang_code}] {jsonl_path.name} → {db_path.name}")
    tmp_path.unlink(missing_ok=True)
    t_start = time.monotonic()

    try:
        conn = sqlite3.connect(tmp_path)
        conn.executescript(SCHEMA)

        batch: list[tuple] = []
        written = 0
        skipped = 0

        with open(jsonl_path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    obj = json.loads(line)
                except json.JSONDecodeError:
                    skipped += 1
                    continue

                word = obj.get("w", "").strip()
                pos = obj.get("p", "").strip()
                defs = obj.get("d")
                example = obj.get("e")

                if not word or not pos or not defs:
                    skipped += 1
                    continue

                batch.append((
                    word,
                    pos,
                    json.dumps(defs, ensure_ascii=False),
                    example if isinstance(example, str) else None,
                ))
                written += 1

                if len(batch) >= BATCH_SIZE:
                    conn.executemany(
                        "INSERT INTO entries VALUES (?,?,?,?)", batch
                    )
                    conn.commit()
                    batch.clear()

        if batch:
            conn.executemany("INSERT INTO entries VALUES (?,?,?,?)", batch)
            conn.commit()

        conn.close()

    except Exception:
        tmp_path.unlink(missing_ok=True)
        raise

    # Atomic rename
    tmp_path.rename(db_path)
    elapsed = time.monotonic() - t_start
    size_mb = db_path.stat().st_size / 1024 / 1024
    print(f"  Done: {written:,} entries, {skipped:,} skipped in {elapsed:.1f}s → {size_mb:.1f} MB")


def available_langs() -> list[str]:
    return sorted(p.stem for p in OUTPUT_DIR.glob("*.jsonl"))


def main() -> None:
    parser = argparse.ArgumentParser(description="basic JSONL → SQLite")
    parser.add_argument(
        "--lang",
        nargs="*",
        metavar="CODE",
        help="Language code(s) to convert (default: all *.jsonl in output/)",
    )
    args = parser.parse_args()

    codes = args.lang if args.lang else available_langs()
    if codes == ["all"]:
        codes = available_langs()
    if not codes:
        print("No JSONL files found in output/. Run step1 first.", file=sys.stderr)
        sys.exit(1)

    for code in codes:
        convert(code)

    print("\nAll done.")


if __name__ == "__main__":
    main()
