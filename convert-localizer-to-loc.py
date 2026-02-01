#!/usr/bin/env python3
"""
Automated @Localizer to <loc> tag converter for Razor pages.

This script converts @Localizer["..."] instances to <loc key="..." /> tags
and converts HTML attributes to use the loc-* pattern.

Usage:
    python convert-localizer-to-loc.py <file_path> [--dry-run]
"""

import re
import sys
import argparse
from pathlib import Path

# Fix Windows console encoding issues
if sys.platform == 'win32':
    import os
    os.system('chcp 65001 >nul 2>&1')
    sys.stdout.reconfigure(encoding='utf-8')

def convert_attribute_localizer(content):
    """
    Convert HTML attributes using @Localizer to loc-* attributes.

    Examples:
        title="@Localizer["Key"]" → loc-title="Key"
        placeholder="@Localizer["Key"]" → loc-placeholder="Key"
        aria-label="@Localizer["Key"]" → loc-aria-label="Key"
    """

    # Pattern for attributes: attribute="@Localizer["Key"]"
    attr_pattern = r'(title|placeholder|aria-label|aria-description)="@Localizer\["([^"]+)"\]"'

    def replace_attr(match):
        attr_name = match.group(1)
        key = match.group(2)

        # Map attribute names to loc-* equivalents
        loc_attr = f"loc-{attr_name}"

        return f'{loc_attr}="{key}"'

    content = re.sub(attr_pattern, replace_attr, content)

    return content

def convert_body_localizer(content):
    """
    Convert body text @Localizer instances to <loc> tags.

    Examples:
        @Localizer["Key"] → <loc key="Key" />
        <span>@Localizer["Key"]</span> → <span><loc key="Key" /></span>
    """

    # Pattern for simple body text: @Localizer["Key"]
    # Must NOT be inside an attribute (no preceding =")
    simple_pattern = r'(?<!")@Localizer\["([^"]+)"\]'

    def replace_simple(match):
        key = match.group(1)
        return f'<loc key="{key}" />'

    content = re.sub(simple_pattern, replace_simple, content)

    return content

def convert_parameterized_localizer(content):
    """
    Convert parameterized @Localizer instances to <loc> tags with params.

    Examples:
        @Localizer["Key", param1] → <loc key="Key" params='new object[] { param1 }' />
        @Localizer["Key", param1, param2] → <loc key="Key" params='new object[] { param1, param2 }' />
    """

    # Pattern for parameterized: @Localizer["Key", param1, param2]
    # This is more complex - let's handle it conservatively
    param_pattern = r'@Localizer\["([^"]+)",\s*([^\]]+)\]'

    def replace_param(match):
        key = match.group(1)
        params = match.group(2).strip()

        # Convert comma-separated params to array syntax
        return f'<loc key="{key}" params=\'new object[] {{ {params} }}\' />'

    content = re.sub(param_pattern, replace_param, content)

    return content

def remove_localizer_imports(content):
    """
    Remove @inject IStringLocalizer directives if no @Localizer references remain.
    """

    # Check if there are any @Localizer references left
    if '@Localizer' not in content:
        # Remove the @inject line
        content = re.sub(r'@inject\s+IStringLocalizer<SharedResources>\s+Localizer\s*\n', '', content)

        # Remove @using directives if they're only used for Localizer
        if '@Localizer' not in content:
            # Check if Localizer is the only thing using these imports
            content = re.sub(r'@using\s+Microsoft\.Extensions\.Localization\s*\n', '', content)
            content = re.sub(r'@using\s+ShiftManager\.Resources\s*\n', '', content)

    return content

def convert_file(file_path, dry_run=False):
    """
    Convert a single Razor file from @Localizer to <loc> tags.
    """

    print(f"\n{'[DRY RUN] ' if dry_run else ''}Processing: {file_path}")

    # Read file content
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            original_content = f.read()
    except Exception as e:
        print(f"  ❌ Error reading file: {e}")
        return False

    content = original_content

    # Track changes
    changes = []

    # Step 1: Convert attributes (must happen BEFORE body text conversion)
    new_content = convert_attribute_localizer(content)
    if new_content != content:
        attr_count = len(re.findall(r'loc-(title|placeholder|aria-label|aria-description)', new_content)) - \
                     len(re.findall(r'loc-(title|placeholder|aria-label|aria-description)', content))
        changes.append(f"  [+] Converted {attr_count} attribute(s)")
        content = new_content

    # Step 2: Convert parameterized @Localizer (before simple ones)
    new_content = convert_parameterized_localizer(content)
    param_count = new_content.count('<loc key=') - content.count('<loc key=')
    if param_count > 0:
        changes.append(f"  [+] Converted {param_count} parameterized string(s)")
        content = new_content

    # Step 3: Convert simple body text
    new_content = convert_body_localizer(content)
    body_count = new_content.count('<loc key=') - content.count('<loc key=')
    if body_count > 0:
        changes.append(f"  [+] Converted {body_count} body text instance(s)")
        content = new_content

    # Step 4: Clean up imports
    new_content = remove_localizer_imports(content)
    if new_content != content:
        changes.append(f"  [+] Removed unused @inject/@using directives")
        content = new_content

    # Check if anything changed
    if content == original_content:
        print("  [i] No changes needed")
        return True

    # Print changes
    for change in changes:
        print(change)

    # Count remaining @Localizer instances
    remaining = len(re.findall(r'@Localizer\[', content))
    if remaining > 0:
        print(f"  [!] {remaining} @Localizer instance(s) remain (likely complex/conditional)")

    # Write changes (if not dry run)
    if not dry_run:
        try:
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(content)
            print(f"  [OK] File updated successfully")
        except Exception as e:
            print(f"  [ERROR] Error writing file: {e}")
            return False
    else:
        print(f"  [i] Dry run - no changes written")

    return True

def main():
    parser = argparse.ArgumentParser(description='Convert @Localizer to <loc> tags in Razor files')
    parser.add_argument('files', nargs='+', help='File path(s) to convert')
    parser.add_argument('--dry-run', action='store_true', help='Preview changes without modifying files')
    parser.add_argument('--verbose', action='store_true', help='Show detailed output')

    args = parser.parse_args()

    success_count = 0
    fail_count = 0

    for file_path in args.files:
        path = Path(file_path)

        if not path.exists():
            print(f"\n[ERROR] File not found: {file_path}")
            fail_count += 1
            continue

        if not path.suffix in ['.cshtml', '.razor']:
            print(f"\n[!] Skipping non-Razor file: {file_path}")
            continue

        if convert_file(path, dry_run=args.dry_run):
            success_count += 1
        else:
            fail_count += 1

    # Summary
    print(f"\n{'='*60}")
    print(f"Summary: {success_count} file(s) processed successfully, {fail_count} failed")
    if args.dry_run:
        print("(Dry run - no files were modified)")
    print(f"{'='*60}")

if __name__ == '__main__':
    main()
