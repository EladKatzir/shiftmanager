#!/bin/bash
# Tokenization Verification Script (A-009-EXT)
# Usage: ./verify-tokenization.sh <file-or-directory>

set -e

PATH_ARG="${1:-.}"

echo "=== Tokenization Verification ==="

# Find files
if [ -f "$PATH_ARG" ]; then
    FILES="$PATH_ARG"
else
    FILES=$(find "$PATH_ARG" -type f \( -name "*.cshtml" -o -name "*.css" \) ! -name "tokens.css" ! -name "*.min.css")
fi

TOTAL_HEX=0
TOTAL_RGBA=0
FAILED=0

for FILE in $FILES; do
    HEX_COUNT=$(grep -cE '#[0-9A-Fa-f]{3,8}\b' "$FILE" 2>/dev/null || echo 0)
    RGBA_COUNT=$(grep -c 'rgba\?' "$FILE" 2>/dev/null || echo 0)

    if [ "$HEX_COUNT" -eq 0 ] && [ "$RGBA_COUNT" -eq 0 ]; then
        echo -e "\e[32mPASS\e[0m: $FILE"
    else
        echo -e "\e[31mFAIL\e[0m: $FILE - $HEX_COUNT hex, $RGBA_COUNT rgba"
        TOTAL_HEX=$((TOTAL_HEX + HEX_COUNT))
        TOTAL_RGBA=$((TOTAL_RGBA + RGBA_COUNT))
        FAILED=$((FAILED + 1))
    fi
done

echo ""
echo "=== Summary ==="
if [ "$FAILED" -eq 0 ]; then
    echo -e "\e[32mAll files passed!\e[0m"
    exit 0
else
    echo -e "\e[31mFailed files: $FAILED\e[0m"
    echo -e "\e[31mTotal: $TOTAL_HEX hex, $TOTAL_RGBA rgba\e[0m"
    exit 1
fi
