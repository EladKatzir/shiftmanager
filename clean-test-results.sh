#!/bin/bash
# ============================================================
# ShiftManager — Clean Test Results
# Deletes all test output/artifacts WITHOUT touching:
#   - Source code (.cs, .js, .cshtml, etc.)
#   - Visual regression baseline snapshots (*-snapshots/)
#   - Test helper/fixture files
#   - Node modules or config files
# ============================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== ShiftManager Test Results Cleanup ==="
echo ""

# Track what we delete
DELETED=0

# -------------------------------------------------------
# 1. .NET test results (xUnit / coverlet output)
# -------------------------------------------------------
if [ -d "ShiftManager.Tests/TestResults" ]; then
    echo "[.NET] Removing ShiftManager.Tests/TestResults/"
    rm -rf "ShiftManager.Tests/TestResults"
    DELETED=$((DELETED + 1))
fi

# -------------------------------------------------------
# 2. Playwright smoke/feature test artifacts
#    Config outputs to: qa-automation/reports/
# -------------------------------------------------------
if [ -d "qa-automation/reports/test-artifacts" ]; then
    echo "[Playwright] Removing qa-automation/reports/test-artifacts/"
    rm -rf "qa-automation/reports/test-artifacts"
    DELETED=$((DELETED + 1))
fi

if [ -d "qa-automation/reports/playwright-report" ]; then
    echo "[Playwright] Removing qa-automation/reports/playwright-report/"
    rm -rf "qa-automation/reports/playwright-report"
    DELETED=$((DELETED + 1))
fi

if [ -f "qa-automation/reports/test-results.json" ]; then
    echo "[Playwright] Removing qa-automation/reports/test-results.json"
    rm -f "qa-automation/reports/test-results.json"
    DELETED=$((DELETED + 1))
fi

if [ -f "qa-automation/reports/efficiency-report.json" ]; then
    echo "[Playwright] Removing qa-automation/reports/efficiency-report.json"
    rm -f "qa-automation/reports/efficiency-report.json"
    DELETED=$((DELETED + 1))
fi

# -------------------------------------------------------
# 3. Production QA test artifacts
#    Config outputs to: ProductionReady/
# -------------------------------------------------------
if [ -d "ProductionReady/test-artifacts" ]; then
    echo "[Production QA] Removing ProductionReady/test-artifacts/"
    rm -rf "ProductionReady/test-artifacts"
    DELETED=$((DELETED + 1))
fi

if [ -d "ProductionReady/playwright-report" ]; then
    echo "[Production QA] Removing ProductionReady/playwright-report/"
    rm -rf "ProductionReady/playwright-report"
    DELETED=$((DELETED + 1))
fi

if [ -f "ProductionReady/test-results.json" ]; then
    echo "[Production QA] Removing ProductionReady/test-results.json"
    rm -f "ProductionReady/test-results.json"
    DELETED=$((DELETED + 1))
fi

# -------------------------------------------------------
# 4. Playwright auth state (login session cache)
#    global-setup.js saves to playwright/.auth/owner.json
#    Playwright 1.57 auto-creates parent dirs via mkdirIfNeeded,
#    so no need to re-create the directory after cleaning.
# -------------------------------------------------------
if [ -d "qa-automation/playwright" ]; then
    echo "[Playwright] Removing qa-automation/playwright/ (auth state)"
    rm -rf "qa-automation/playwright"
    DELETED=$((DELETED + 1))
fi

# -------------------------------------------------------
# 5. Playwright .last-run.json (remembers last failed test)
# -------------------------------------------------------
if [ -f "qa-automation/.last-run.json" ]; then
    echo "[Playwright] Removing qa-automation/.last-run.json"
    rm -f "qa-automation/.last-run.json"
    DELETED=$((DELETED + 1))
fi

# -------------------------------------------------------
# 6. Stale root-level test-results.json in qa-automation/
#    (duplicate of reports/test-results.json)
# -------------------------------------------------------
if [ -f "qa-automation/test-results.json" ]; then
    echo "[Playwright] Removing qa-automation/test-results.json"
    rm -f "qa-automation/test-results.json"
    DELETED=$((DELETED + 1))
fi

# -------------------------------------------------------
# 7. Headed-mode debug logs
# -------------------------------------------------------
if [ -f "qa-automation/blueprint-test-headed.log" ]; then
    echo "[Playwright] Removing qa-automation/blueprint-test-headed.log"
    rm -f "qa-automation/blueprint-test-headed.log"
    DELETED=$((DELETED + 1))
fi

# -------------------------------------------------------
# Summary
# -------------------------------------------------------
echo ""
if [ "$DELETED" -eq 0 ]; then
    echo "Nothing to clean — all test results already removed."
else
    echo "Done! Cleaned $DELETED item(s)."
fi
echo ""
echo "Preserved:"
echo "  - All source code"
echo "  - Visual regression snapshots (*-snapshots/)"
echo "  - Test fixtures, helpers, and config files"
echo "  - node_modules/"
