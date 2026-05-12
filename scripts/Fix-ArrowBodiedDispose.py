"""Fixup for the bulk migration: convert arrow-bodied Dispose() to block-bodied
when the migration script inserted a follow-up _sqliteConnection.Dispose() line.

Before:
    public void Dispose() => _db.Dispose();
     _sqliteConnection.Dispose();

After:
    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }
"""
import re
from pathlib import Path
import sys

# Match: arrow-bodied Dispose followed by an orphan _sqliteConnection.Dispose() line.
PATTERN = re.compile(
    r"(?P<indent>[ \t]*)public\s+void\s+Dispose\(\)\s*=>\s*(?P<inner>_db\??\.Dispose\(\))\s*;\s*\n"
    r"\s*_sqliteConnection\.Dispose\(\)\s*;"
)


def fix_file(path: Path) -> bool:
    text = path.read_text(encoding="utf-8")
    def repl(m):
        indent = m.group("indent")
        inner = m.group("inner")
        return (
            f"{indent}public void Dispose()\n"
            f"{indent}{{\n"
            f"{indent}    {inner};\n"
            f"{indent}    _sqliteConnection.Dispose();\n"
            f"{indent}}}"
        )
    new_text, n = PATTERN.subn(repl, text)
    if n > 0:
        path.write_text(new_text, encoding="utf-8")
        return True
    return False


def main():
    root = Path(sys.argv[1])
    fixed = []
    for cs_file in root.rglob("*.cs"):
        if fix_file(cs_file):
            fixed.append(str(cs_file.relative_to(root)))
    print(f"FIXED ({len(fixed)}):")
    for f in fixed:
        print(f"  {f}")


if __name__ == "__main__":
    main()
