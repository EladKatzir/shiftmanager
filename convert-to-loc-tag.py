#!/usr/bin/env python3
"""
Convert @Localizer["Key"] to <loc key="Key" /> in Razor .cshtml files
Handles multiple patterns while preserving JavaScript, attributes, and code blocks
"""

import re
import os
import sys
from pathlib import Path

# Patterns to convert (HTML content only)
PATTERNS_TO_CONVERT = [
    # Pattern 1: Between HTML tags >@Localizer["Key"]<
    (r'(>)\s*@Localizer\["([^"]+)"\]\s*(<)', r'\1<loc key="\2" />\3'),

    # Pattern 2: After opening tag <tag>@Localizer["Key"]
    (r'(<(?:h[1-6]|p|div|span|label|td|th|li|button)[^>]*>)\s*@Localizer\["([^"]+)"\]', r'\1<loc key="\2" />'),

    # Pattern 3: Before closing tag @Localizer["Key"]</tag>
    (r'@Localizer\["([^"]+)"\]\s*(</(?:h[1-6]|p|div|span|label|td|th|li|button)>)', r'<loc key="\1" />\2'),
]

# Patterns to SKIP (keep as @Localizer)
SKIP_PATTERNS = [
    r'ViewData\[',  # ViewData["Title"] = Localizer[...]
    r'new BreadcrumbItem',  # Breadcrumb { Label = Localizer[...] }
    r'placeholder="@Localizer',  # Attributes
    r'title="@Localizer',  # Attributes
    r'alt="@Localizer',  # Attributes
    r'aria-label="@Localizer',  # Attributes
    r'onclick="',  # JavaScript
    r'onsubmit="',  # JavaScript
    r'const\s+\w+\s*=\s*',  # JavaScript variables
    r'\.replace\(',  # JavaScript string operations
    r'confirm\(',  # JavaScript confirm dialogs
    r'@Localizer\[\w+\.',  # Dynamic keys like @Localizer[role.ToString()]
    r'@Localizer\[(?!")[^\]]*\]',  # Non-string-literal keys
]

def should_skip_line(line):
    """Check if line contains patterns that should not be converted"""
    for pattern in SKIP_PATTERNS:
        if re.search(pattern, line):
            return True
    return False

def convert_file(file_path, dry_run=False):
    """Convert a single file"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content
        lines = content.split('\n')
        converted_lines = []
        replacements = 0

        for line in lines:
            original_line = line

            # Skip lines with patterns that shouldn't be converted
            if should_skip_line(line):
                converted_lines.append(line)
                continue

            # Apply conversion patterns
            for pattern, replacement in PATTERNS_TO_CONVERT:
                new_line = re.sub(pattern, replacement, line)
                if new_line != line:
                    replacements += (len(re.findall(pattern, line)))
                    line = new_line

            converted_lines.append(line)

        new_content = '\n'.join(converted_lines)

        # Remove @using and @inject if no more @Localizer references
        if '@Localizer[' not in new_content:
            new_content = re.sub(r'@using Microsoft\.Extensions\.Localization\n', '', new_content)
            new_content = re.sub(r'@using ShiftManager\.Resources\n', '', new_content)
            new_content = re.sub(r'@inject IStringLocalizer<SharedResources> Localizer\n', '', new_content)

        if new_content != original_content:
            if not dry_run:
                with open(file_path, 'w', encoding='utf-8') as f:
                    f.write(new_content)
            return True, replacements

        return False, 0

    except Exception as e:
        print(f"ERROR processing {file_path}: {e}")
        return False, 0

def main():
    dry_run = '--dry-run' in sys.argv or '-n' in sys.argv

    pages_dir = Path('Pages')
    if not pages_dir.exists():
        print("ERROR: Pages directory not found. Run from ShiftManager root directory.")
        sys.exit(1)

    # Find all .cshtml files (excluding partials starting with _)
    cshtml_files = [
        f for f in pages_dir.rglob('*.cshtml')
        if not f.name.startswith('_') or f.name == '_TimelineItem.cshtml'
    ]

    print(f"Found {len(cshtml_files)} .cshtml files")
    print(f"Mode: {'DRY RUN' if dry_run else 'LIVE'}\n")

    files_modified = 0
    total_replacements = 0

    for file_path in sorted(cshtml_files):
        modified, replacements = convert_file(file_path, dry_run)
        if modified:
            files_modified += 1
            total_replacements += replacements
            relative_path = file_path.relative_to(pages_dir.parent)
            print(f"[MODIFIED] {relative_path} - {replacements} replacements")

    print(f"\n{'='*50}")
    print(f"Files modified: {files_modified}")
    print(f"Total replacements: {total_replacements}")

    if dry_run:
        print(f"\nDRY RUN - No files were changed")
        print(f"Run without --dry-run to apply changes")
    else:
        print(f"\nFiles updated!")
        print(f"Run 'dotnet build' to verify")

if __name__ == '__main__':
    main()
