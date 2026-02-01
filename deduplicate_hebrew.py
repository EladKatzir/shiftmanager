import xml.etree.ElementTree as ET
from collections import defaultdict
import shutil

def deduplicate_resx(file_path):
    """Deduplicate .resx file, keeping FIRST occurrence of each key."""

    # Create backup
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

    print(f"Processing: {file_path}")
    print(f"Total data elements: {len(data_elements)}")

    # Track duplicates for reporting
    duplicates_count = 0

    for elem in data_elements:
        name = elem.get('name')

        if name in seen_keys:
            # This is a duplicate - mark for removal
            elements_to_remove.append(elem)
            duplicates_count += 1
        else:
            # First occurrence - keep it
            seen_keys.add(name)

    print(f"Duplicates to remove: {duplicates_count}")

    # Remove duplicates from XML tree
    for elem in elements_to_remove:
        parent = root
        parent.remove(elem)

    # Write back to file with proper formatting
    tree.write(file_path, encoding='utf-8', xml_declaration=True)

    print(f"File updated: {file_path}")
    print(f"Removed {len(elements_to_remove)} duplicate entries")
    print(f"Remaining unique keys: {len(seen_keys)}")

    return len(elements_to_remove)


if __name__ == "__main__":
    file_path = 'Resources/SharedResources.he-IL.resx'
    removed = deduplicate_resx(file_path)
    print(f"\nDONE: Removed {removed} duplicates from Hebrew resources")
