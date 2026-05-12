"""
Bulk migration script: convert UseInMemoryDatabase test fixtures to real SQLite.

The 2026-05-11 GRIFFIN-USERLOOKUP-510 bug was hidden by the In-Memory provider executing
LINQ in C# (no SQL translation). This script migrates each test fixture to real SQLite so
the production SQL translator runs end-to-end — any future ToLowerInvariant() / Contains(
StringComparison.X) / Regex.IsMatch / etc. in an IQueryable fails at test time.

`Foreign Keys=False` preserves the relaxed FK semantics existing fixtures rely on (many
reference CompanyId/UserId without seeding parents). Real-SQLite fixtures designed from
scratch leave FKs ON.

Idempotent: re-running on an already-migrated file is a no-op.
"""
import re
import sys
from pathlib import Path

# Regex to find the In-Memory DbContext setup. Spans the multi-line builder block
# from `var options = new DbContextOptionsBuilder<AppDbContext>()` through `.Options;`.
# Uses [\s\S]*? for lazy any-char including newlines (DOTALL would also work, but the
# character class form is more explicit). Tolerates ConfigureWarnings or any other
# intermediate chained method call between UseInMemoryDatabase and .Options.
INMEM_PATTERN = re.compile(
    r"(?P<indent>[ \t]+)var\s+options\s*=\s*new\s+DbContextOptionsBuilder<AppDbContext>\(\)"
    r"[\s\S]*?\.UseInMemoryDatabase[\s\S]*?\.Options\s*;",
)

# Field declaration we'll add. Goes right above `private readonly AppDbContext _db;`
FIELD_DECL_PATTERN = re.compile(
    r"(?P<indent>[ \t]+)private\s+readonly\s+AppDbContext\s+_db\s*;"
)

# Dispose body — find the line that calls _db.Dispose() and insert _sqliteConnection.Dispose() after.
DISPOSE_DB_PATTERN = re.compile(
    r"(?P<line>(?P<indent>[ \t]+)_db\??\.Dispose\(\)\s*;)"
)

USING_EF_CORE = re.compile(r"^using\s+Microsoft\.EntityFrameworkCore\s*;\s*$", re.MULTILINE)


def migrate_file(path: Path) -> str:
    """Return 'migrated', 'skipped' (already migrated), or 'no-pattern'."""
    text = path.read_text(encoding="utf-8")
    original = text

    # Skip if already has SqliteConnection field (signal of prior migration)
    if "_sqliteConnection" in text or "Microsoft.Data.Sqlite" in text and "UseSqlite" in text:
        return "skipped"

    # Skip if no UseInMemoryDatabase usage (e.g., comments only)
    if "UseInMemoryDatabase" not in text:
        return "no-pattern"

    # 1. Add `using Microsoft.Data.Sqlite;` after the EntityFrameworkCore using.
    if "using Microsoft.Data.Sqlite;" not in text:
        match = USING_EF_CORE.search(text)
        if not match:
            return "no-pattern"
        text = (text[:match.end()] + "\nusing Microsoft.Data.Sqlite;" + text[match.end():])

    # 2. Replace the DbContext options builder block.
    def replace_inmem(m):
        indent = m.group("indent")
        # Strip any ConfigureWarnings — irrelevant for SQLite.
        return (
            f"{indent}_sqliteConnection = new SqliteConnection(\"DataSource=:memory:;Foreign Keys=False\");\n"
            f"{indent}_sqliteConnection.Open();\n"
            f"{indent}var options = new DbContextOptionsBuilder<AppDbContext>()\n"
            f"{indent}    .UseSqlite(_sqliteConnection)\n"
            f"{indent}    .Options;"
        )

    new_text, n_repl = INMEM_PATTERN.subn(replace_inmem, text)
    if n_repl == 0:
        # Fallback: tolerate a different inline form (some files use string interp etc.)
        return "no-pattern"
    text = new_text

    # 3. Insert SqliteConnection field BEFORE the AppDbContext _db field.
    field_match = FIELD_DECL_PATTERN.search(text)
    if field_match:
        indent = field_match.group("indent")
        insertion = f"{indent}private readonly SqliteConnection _sqliteConnection;\n"
        text = text[:field_match.start()] + insertion + text[field_match.start():]

    # 4. Append _sqliteConnection.Dispose() to the Dispose method body.
    # Match the FIRST occurrence to avoid disrupting unrelated `_db.Dispose()` calls.
    dispose_match = DISPOSE_DB_PATTERN.search(text)
    if dispose_match:
        line = dispose_match.group("line")
        indent = dispose_match.group("indent")
        addition = f"{line}\n{indent}_sqliteConnection.Dispose();"
        text = text[:dispose_match.start()] + addition + text[dispose_match.end():]

    if text == original:
        return "no-change"

    # Need to call Database.EnsureCreated() after AppDbContext construction.
    # Pattern: `_db = new AppDbContext(options);` → add EnsureCreated next line.
    text = re.sub(
        r"(?P<indent>[ \t]+)_db\s*=\s*new\s+AppDbContext\(options\)\s*;",
        lambda m: f"{m.group(0)}\n{m.group('indent')}_db.Database.EnsureCreated();",
        text,
        count=1
    )

    path.write_text(text, encoding="utf-8")
    return "migrated"


def main():
    if len(sys.argv) < 2:
        print("Usage: python Migrate-InMemoryToSqlite.py <test-files-root>")
        sys.exit(1)

    root = Path(sys.argv[1])
    results = {"migrated": [], "skipped": [], "no-pattern": [], "no-change": []}

    # Find all .cs files under root that reference UseInMemoryDatabase
    for cs_file in root.rglob("*.cs"):
        if "UseInMemoryDatabase" not in cs_file.read_text(encoding="utf-8"):
            continue
        outcome = migrate_file(cs_file)
        results[outcome].append(str(cs_file.relative_to(root)))

    for outcome, files in results.items():
        print(f"\n{outcome.upper()} ({len(files)}):")
        for f in files:
            print(f"  {f}")


if __name__ == "__main__":
    main()
