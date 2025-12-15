import xml.etree.ElementTree as ET
from collections import defaultdict

def analyze_duplicates(file_path, lang):
    tree = ET.parse(file_path)
    root = tree.getroot()

    # Find all data elements
    data_elements = root.findall('.//data')

    # Group by name
    keys = defaultdict(list)
    for idx, elem in enumerate(data_elements):
        name = elem.get('name')
        value = elem.find('value')
        if value is not None:
            keys[name].append({
                'index': idx,
                'value': value.text or '',
                'element': elem
            })

    # Find duplicates
    duplicates = {k: v for k, v in keys.items() if len(v) > 1}

    print(f"\n{'='*80}")
    print(f"DUPLICATE ANALYSIS: {lang}")
    print(f"{'='*80}\n")
    print(f"Total unique keys: {len(keys)}")
    print(f"Keys with duplicates: {len(duplicates)}")
    print(f"\n{'='*80}\n")

    # Analyze each duplicate
    for key_name in sorted(duplicates.keys()):
        occurrences = duplicates[key_name]
        print(f"Key: '{key_name}' - {len(occurrences)} occurrences")

        # Check if all values are identical
        values = [occ['value'] for occ in occurrences]
        all_same = len(set(values)) == 1

        if all_same:
            print(f"  Status: ALL IDENTICAL [OK]")
            print(f"  Value: {values[0][:100]}")
        else:
            print(f"  Status: DIFFERENT VALUES [WARNING]")
            for i, occ in enumerate(occurrences):
                val = occ['value'][:80]
                print(f"  [{i+1}] {val}")

        print()

    return duplicates, keys

# Analyze both files
en_duplicates, en_keys = analyze_duplicates('Resources/SharedResources.resx', 'English')
he_duplicates, he_keys = analyze_duplicates('Resources/SharedResources.he-IL.resx', 'Hebrew')

print("\n" + "="*80)
print("COMPARISON SUMMARY")
print("="*80)
print(f"English: {len(en_keys)} unique keys, {len(en_duplicates)} with duplicates")
print(f"Hebrew: {len(he_keys)} unique keys, {len(he_duplicates)} with duplicates")
print("\nKeys only in English:", set(en_keys.keys()) - set(he_keys.keys()))
print("\nKeys only in Hebrew:", set(he_keys.keys()) - set(en_keys.keys()))
