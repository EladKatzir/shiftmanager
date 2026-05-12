"""For each constructor that uses _sqliteConnection (post-migration), ensure
_db.Database.EnsureCreated() runs after `_db = new AppDbContext(options);`.
The main migration script only patched the FIRST occurrence per file."""
import re
from pathlib import Path
import sys

# Find every `_db = new AppDbContext(options);` line that isn't already followed
# by an EnsureCreated() call.
PATTERN = re.compile(
    r"(?P<line>(?P<indent>[ \t]+)_db\s*=\s*new\s+AppDbContext\(options\)\s*;)"
    r"(?!\s*\n\s*_db\.Database\.EnsureCreated)"
)

def fix_file(path):
    text = path.read_text(encoding="utf-8")
    if "_sqliteConnection" not in text:
        return False  # skip non-migrated files

    def repl(m):
        return f"{m.group('line')}\n{m.group('indent')}_db.Database.EnsureCreated();"

    new_text, n = PATTERN.subn(repl, text)
    if n > 0:
        path.write_text(new_text, encoding="utf-8")
        return n
    return 0


def main():
    root = Path(sys.argv[1])
    for cs_file in root.rglob("*.cs"):
        n = fix_file(cs_file)
        if n:
            print(f"  +{n} EnsureCreated calls in {cs_file.relative_to(root)}")


if __name__ == "__main__":
    main()
