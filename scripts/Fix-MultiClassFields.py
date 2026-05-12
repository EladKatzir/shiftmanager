"""Some test files have multiple test classes in one file. The bulk migration script
only added the `_sqliteConnection` field to the first class. This fixup adds the field
to every class that uses `_sqliteConnection = new SqliteConnection(...)` but doesn't
have the field declared yet, and also patches each class's Dispose method to clean it up.
"""
import re
from pathlib import Path
import sys


def find_classes(text):
    """Return list of (class_name, body_start_offset) for top-level test classes."""
    classes = []
    pat = re.compile(r"^public\s+class\s+(\w+)\b[^{]*\{", re.MULTILINE)
    for m in pat.finditer(text):
        classes.append((m.group(1), m.start(), m.end()))
    return classes


def find_class_body_end(text, start):
    """Given the offset where the class body opens with `{`, find the matching close."""
    depth = 0
    i = start - 1  # back to the `{`
    while text[i] != "{":
        i -= 1
    depth = 1
    j = i + 1
    while j < len(text) and depth > 0:
        if text[j] == "{":
            depth += 1
        elif text[j] == "}":
            depth -= 1
        j += 1
    return j


def patch_class(text, body_start, body_end):
    """For one class span, ensure SqliteConnection field is declared and Dispose
    cleans it up. Returns (new_text_for_this_class, replaced)."""
    body = text[body_start:body_end]
    used = "_sqliteConnection = new SqliteConnection" in body or \
           "_sqliteConnection.Open()" in body
    declared = "private readonly SqliteConnection _sqliteConnection" in body
    if not used or declared:
        return body, False

    # Insert the field declaration right before the AppDbContext _db field.
    db_field = re.search(r"(?P<indent>[ \t]+)private\s+readonly\s+AppDbContext\s+_db\b", body)
    if db_field:
        indent = db_field.group("indent")
        body = (body[:db_field.start()] +
                f"{indent}private readonly SqliteConnection _sqliteConnection;\n" +
                body[db_field.start():])

    # Append _sqliteConnection.Dispose() to the FIRST Dispose() body in this class.
    dispose_m = re.search(r"(?P<indent>[ \t]+)_db\??\.Dispose\(\)\s*;", body)
    if dispose_m:
        # Skip if _sqliteConnection.Dispose already present after _db.Dispose
        after = body[dispose_m.end():dispose_m.end() + 200]
        if "_sqliteConnection.Dispose" not in after:
            indent = dispose_m.group("indent")
            body = (body[:dispose_m.end()] +
                    f"\n{indent}_sqliteConnection.Dispose();" +
                    body[dispose_m.end():])

    return body, True


def fix_file(path):
    text = path.read_text(encoding="utf-8")
    classes = find_classes(text)
    if len(classes) < 2:
        return False

    # Process classes in reverse so offsets stay valid.
    new_text = text
    changed = False
    # Re-find class spans after each patch
    while True:
        text_now = new_text
        classes_now = find_classes(text_now)
        applied = False
        for cls_name, cls_start, body_open_brace_offset in reversed(classes_now):
            body_end = find_class_body_end(text_now, body_open_brace_offset)
            body_start = body_open_brace_offset
            body = text_now[body_start:body_end]
            patched, did = patch_class(text_now, body_start, body_end)
            if did:
                new_text = text_now[:body_start] + patched + text_now[body_end:]
                applied = True
                changed = True
                break
        if not applied:
            break

    if changed:
        path.write_text(new_text, encoding="utf-8")
    return changed


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
