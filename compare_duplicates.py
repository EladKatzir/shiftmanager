import xml.etree.ElementTree as ET
from collections import defaultdict

def analyze_different_values(file_path):
    """Find duplicate keys with DIFFERENT values."""

    tree = ET.parse(file_path)
    root = tree.getroot()
    data_elements = root.findall('.//data')

    # Group by name
    keys = defaultdict(list)
    for idx, elem in enumerate(data_elements):
        name = elem.get('name')
        value_elem = elem.find('value')
        value = value_elem.text if value_elem is not None else ''
        keys[name].append({
            'index': idx,
            'value': value,
        })

    # Find duplicates with different values
    different_values = {}
    for key_name, occurrences in keys.items():
        if len(occurrences) > 1:
            values = [occ['value'] for occ in occurrences]
            if len(set(values)) > 1:  # Different values
                different_values[key_name] = occurrences

    return different_values


def main():
    print("="*80)
    print("DUPLICATE KEYS WITH DIFFERENT VALUES - DECISION REQUIRED")
    print("="*80)
    print("\nThese keys have multiple definitions with DIFFERENT values.")
    print("The deduplication script will keep the FIRST occurrence.\n")
    print("="*80 + "\n")

    file_path = 'Resources/SharedResources.resx'
    different = analyze_different_values(file_path)

    if not different:
        print("✓ No duplicate keys with different values found!")
        return

    print(f"Found {len(different)} keys with conflicting values:\n")

    for i, (key_name, occurrences) in enumerate(sorted(different.items()), 1):
        print(f"{i}. Key: '{key_name}'")
        print(f"   Occurrences: {len(occurrences)}")

        for j, occ in enumerate(occurrences):
            status = " [WILL BE KEPT]" if j == 0 else " [WILL BE REMOVED]"
            value = occ['value']
            # Truncate long values
            if len(value) > 100:
                value = value[:97] + "..."

            print(f"   [{j+1}] {status}")
            print(f"       Value: \"{value}\"")

        print()

    print("="*80)
    print("RECOMMENDATION")
    print("="*80)
    print("\nThe script will keep the FIRST occurrence of each key.")
    print("This is typically correct as it represents the original definition.")
    print("\nKeys with different values that will be affected:")

    for key_name in sorted(different.keys()):
        first_value = different[key_name][0]['value'][:50]
        print(f"  - {key_name}: \"{first_value}...\"")

    print("\n" + "="*80)


if __name__ == "__main__":
    main()
