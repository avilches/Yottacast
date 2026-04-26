#!/usr/bin/env python3
"""
Step 1: Download kaikki.org native-edition JSONL and produce a lean basic JSONL per language.

Each language is extracted from its own native Wiktionary edition (eswiktionary for Spanish,
dewiktionary for German, etc.), so definitions are in the native language, not in English.

Output format (one line per word+pos):
  {"w":"casa","p":"Noun","d":["def1","def2"],"e":"optional example"}

Usage:
  python step1_kaikki_to_json.py              # process all 16 languages
  python step1_kaikki_to_json.py --lang es    # process only Spanish
  python step1_kaikki_to_json.py --lang es de # process Spanish and German

Output: output/{lang_code}.jsonl
Cache:  cache/{lang_code}.jsonl.gz  (raw kaikki JSONL gz, kept to avoid re-downloading)
"""

import argparse
import gzip
import json
import sys
import urllib.request
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent
CACHE_DIR = SCRIPT_DIR / "cache"
OUTPUT_DIR = SCRIPT_DIR / "output"

CACHE_DIR.mkdir(exist_ok=True)
OUTPUT_DIR.mkdir(exist_ok=True)

# Language code → display name
# Downloads from: https://kaikki.org/downloads/{lang_code}/{lang_code}-extract.jsonl.gz
# Each file contains words from that language's own Wiktionary edition (native definitions).
LANGUAGES: dict[str, str] = {
    "en": "English",
    "es": "Spanish",
    "fr": "French",
    "de": "German",
    "it": "Italian",
    "pt": "Portuguese",
    "ru": "Russian",
    "tr": "Turkish",
    "nl": "Dutch",
    "pl": "Polish",
    "th": "Thai",
    "ko": "Korean",
    "ja": "Japanese",
    "zh": "Chinese",
    "el": "Greek",
    "id": "Indonesian",
}

MAX_DEFINITIONS = 5


def kaikki_url(lang_code: str) -> str:
    return f"https://kaikki.org/downloads/{lang_code}/{lang_code}-extract.jsonl.gz"


def download_with_progress(url: str, dest: Path) -> None:
    print(f"  Downloading {url} ...", flush=True)
    tmp = dest.with_suffix(".tmp")
    try:
        urllib.request.urlretrieve(url, tmp)
        tmp.rename(dest)
    except Exception:
        tmp.unlink(missing_ok=True)
        raise
    print(f"  Saved to {dest}", flush=True)


def is_form_of(sense: dict) -> bool:
    """Return True if this sense is a grammatical inflection, not a real definition."""
    if "form_of" in sense:
        return True
    tags = sense.get("tags", [])
    if isinstance(tags, list) and "form-of" in tags:
        return True
    return False


def extract_entry(obj: dict) -> dict | None:
    """
    Given a kaikki JSONL object, return a compact dict or None if nothing useful.
    """
    word = obj.get("word", "").strip()
    pos = obj.get("pos", "").strip().title()  # normalize: noun → Noun
    if not word or not pos:
        return None

    senses: list[dict] = obj.get("senses", [])
    definitions: list[str] = []
    example: str | None = None

    for sense in senses:
        if is_form_of(sense):
            continue
        glosses: list[str] = sense.get("glosses", [])
        for gloss in glosses:
            gloss = gloss.strip()
            if gloss:
                definitions.append(gloss)
            if len(definitions) >= MAX_DEFINITIONS:
                break

        # Grab the first example we find
        if example is None:
            for ex_obj in sense.get("examples", []):
                text = ex_obj.get("text", "").strip()
                if text:
                    example = text
                    break

        if len(definitions) >= MAX_DEFINITIONS:
            break

    if not definitions:
        return None

    result: dict = {"w": word, "p": pos, "d": definitions}
    if example:
        result["e"] = example
    return result


def process_language(lang_code: str) -> None:
    lang_name = LANGUAGES[lang_code]
    cache_file = CACHE_DIR / f"{lang_code}.jsonl.gz"
    output_file = OUTPUT_DIR / f"{lang_code}.jsonl"

    print(f"\n[{lang_code}] {lang_name}")

    # Download if not cached
    if not cache_file.exists():
        download_with_progress(kaikki_url(lang_code), cache_file)
    else:
        print(f"  Using cached {cache_file.name}")

    # Process (read gzip directly)
    print(f"  Processing → {output_file.name} ...", flush=True)
    written = 0
    skipped = 0

    with (
        gzip.open(cache_file, "rt", encoding="utf-8") as src,
        open(output_file, "w", encoding="utf-8") as dst,
    ):
        for line in src:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                skipped += 1
                continue

            entry = extract_entry(obj)
            if entry is None:
                skipped += 1
                continue

            dst.write(json.dumps(entry, ensure_ascii=False) + "\n")
            written += 1

    print(f"  Done: {written:,} entries written, {skipped:,} skipped")


def main() -> None:
    parser = argparse.ArgumentParser(description="kaikki → basic JSONL")
    parser.add_argument(
        "--lang",
        nargs="*",
        metavar="CODE",
        help="Language code(s) to process (default: all). E.g. --lang es fr de",
    )
    args = parser.parse_args()

    codes = args.lang if args.lang else list(LANGUAGES.keys())
    if codes == ["all"]:
        codes = list(LANGUAGES.keys())
    unknown = [c for c in codes if c not in LANGUAGES]
    if unknown:
        print(f"Unknown language code(s): {', '.join(unknown)}", file=sys.stderr)
        print(f"Available: {', '.join(LANGUAGES)} all", file=sys.stderr)
        sys.exit(1)

    for code in codes:
        process_language(code)

    print("\nAll done.")


if __name__ == "__main__":
    main()
