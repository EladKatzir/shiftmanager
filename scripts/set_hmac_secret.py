#!/usr/bin/env python3
"""
set_hmac_secret.py — Generates a cryptographically random HMAC secret and
writes it to both appsettings.Production.json files.

Usage:
  python scripts/set_hmac_secret.py           # Only replaces the placeholder
  python scripts/set_hmac_secret.py --force   # Overwrites any existing value

Run from the project root (C:\\ShiftManager or the source directory).
"""

import argparse
import base64
import json
import os
import secrets
import sys

PLACEHOLDER = "CHANGE-THIS-TO-A-RANDOM-SECRET"
HMAC_KEY = "ApiKeyHmacSecret"

TARGET_FILES = [
    "appsettings.Production.json",
    os.path.join("FinalProductPublish", "appsettings.Production.json"),
]


def generate_secret() -> str:
    return base64.b64encode(secrets.token_bytes(48)).decode("ascii")


def patch_file(path: str, new_secret: str, force: bool) -> bool:
    """
    Patches ApiKeyHmacSecret in the given JSON file.
    Returns True if the file was updated, False if skipped.
    """
    if not os.path.isfile(path):
        print(f"  [SKIP] Not found: {path}")
        return False

    with open(path, "r", encoding="utf-8") as f:
        content = f.read()

    data = json.loads(content)

    current = data.get(HMAC_KEY, "")
    if current == new_secret:
        print(f"  [SKIP] Already up-to-date: {path}")
        return False

    if not force and current != PLACEHOLDER and current:
        print(f"  [SKIP] Already has a non-placeholder secret: {path}")
        print(f"         Use --force to overwrite.")
        return False

    data[HMAC_KEY] = new_secret

    # Write back with 2-space indent to match the existing file style
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")  # Trailing newline

    print(f"  [OK]   Updated: {path}")
    return True


def main():
    parser = argparse.ArgumentParser(description="Set ApiKeyHmacSecret in production appsettings files.")
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite even if ApiKeyHmacSecret is already set to a non-placeholder value.",
    )
    args = parser.parse_args()

    # Ensure we're running from the project root
    if not os.path.isfile("appsettings.json"):
        print("ERROR: Run this script from the project root (where appsettings.json lives).")
        sys.exit(1)

    new_secret = generate_secret()

    print(f"\nGenerated secret: {new_secret}")
    print("IMPORTANT: Save this secret somewhere safe if you need to rotate it later.\n")
    print("Patching files...")

    updated = 0
    for target in TARGET_FILES:
        if patch_file(target, new_secret, args.force):
            updated += 1

    print(f"\nDone. {updated}/{len(TARGET_FILES)} file(s) updated.")
    if updated == 0:
        print("No files were changed. Use --force to overwrite existing secrets.")


if __name__ == "__main__":
    main()
