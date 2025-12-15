import xml.etree.ElementTree as ET
from collections import defaultdict
import shutil
import os

def deduplicate_resx(file_path, backup=True):
    """
    Carefully deduplicate .resx file, keeping FIRST occurrence of each key.
    Creates backup before modifying.
    """

    # Create backup
    if backup:
        backup_path = file_path + '.backup'
        shutil.copy2(file_path, backup_path)
        print(f"Backup created: {backup_path}")

    # Parse XML
    tree = ET.parse(file_path)
    root = tree.getroot()

    # Track keys we've seen
    seen_keys = set()
    elements_to_remove = []

    # Find all data elements
    data_elements = root.findall('.//data')

    print(f"\nProcessing: {file_path}")
    print(f"Total data elements: {len(data_elements)}")

    # Track duplicates for reporting
    duplicates_removed = {}

    for elem in data_elements:
        name = elem.get('name')
        value_elem = elem.find('value')
        value = value_elem.text if value_elem is not None else ''

        if name in seen_keys:
            # This is a duplicate - mark for removal
            elements_to_remove.append(elem)

            if name not in duplicates_removed:
                duplicates_removed[name] = []
            duplicates_removed[name].append(value)
        else:
            # First occurrence - keep it
            seen_keys.add(name)

    # Report what will be removed
    print(f"\nDuplicates found: {len(duplicates_removed)}")
    print(f"Total duplicate entries to remove: {len(elements_to_remove)}")

    if duplicates_removed:
        print("\nDuplicate keys being removed:")
        for key in sorted(duplicates_removed.keys()):
            values = duplicates_removed[key]
            print(f"  - {key}: {len(values)} duplicate(s)")
            for i, val in enumerate(values):
                val_preview = val[:60] if val else '(empty)'
                print(f"      [{i+1}] {val_preview}")

    # Remove duplicates from XML tree
    for elem in elements_to_remove:
        parent = root
        parent.remove(elem)

    # Write back to file with proper formatting
    tree.write(file_path, encoding='utf-8', xml_declaration=True)

    print(f"\n✓ File updated: {file_path}")
    print(f"  Removed {len(elements_to_remove)} duplicate entries")
    print(f"  Remaining unique keys: {len(seen_keys)}")

    return len(elements_to_remove), duplicates_removed


def verify_no_duplicates(file_path):
    """Verify file has no duplicates after cleanup."""
    tree = ET.parse(file_path)
    root = tree.getroot()
    data_elements = root.findall('.//data')

    keys = [elem.get('name') for elem in data_elements]
    unique_keys = set(keys)

    if len(keys) == len(unique_keys):
        print(f"✓ VERIFICATION PASSED: No duplicates in {file_path}")
        return True
    else:
        duplicates = len(keys) - len(unique_keys)
        print(f"✗ VERIFICATION FAILED: {duplicates} duplicates still exist in {file_path}")
        return False


if __name__ == "__main__":
    print("="*80)
    print("RESOURCE FILE DEDUPLICATION")
    print("="*80)
    print("\nThis script will:")
    print("1. Create backups (.backup files)")
    print("2. Remove duplicate keys (keeping FIRST occurrence)")
    print("3. Verify no duplicates remain")
    print("\n" + "="*80 + "\n")

    files = [
        'Resources/SharedResources.resx',
        'Resources/SharedResources.he-IL.resx'
    ]

    for file_path in files:
        if not os.path.exists(file_path):
            print(f"⚠ File not found: {file_path}")
            continue

        removed_count, duplicates = deduplicate_resx(file_path, backup=True)
        print()

    print("\n" + "="*80)
    print("VERIFICATION")
    print("="*80 + "\n")

    all_passed = True
    for file_path in files:
        if os.path.exists(file_path):
            if not verify_no_duplicates(file_path):
                all_passed = False
            print()

    if all_passed:
        print("\n✓✓✓ ALL FILES VERIFIED - NO DUPLICATES REMAINING ✓✓✓\n")
        print("Backup files created:")
        for file_path in files:
            backup = file_path + '.backup'
            if os.path.exists(backup):
                print(f"  - {backup}")
        print("\nIf everything looks good, you can delete the .backup files.")
    else:
        print("\n✗✗✗ VERIFICATION FAILED - PLEASE REVIEW ✗✗✗\n")
