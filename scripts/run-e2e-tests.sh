#!/bin/bash
set -e

echo "=== ShiftManager E2E Test Runner ==="

# Build
echo "[1/5] Building ShiftManager..."
dotnet build --configuration Release --no-restore 2>&1 | tail -3

# Start app in background
echo "[2/5] Starting app..."
dotnet run --no-build --configuration Release --urls "http://localhost:5000" &
APP_PID=$!

# Wait for health check
echo "[3/5] Waiting for app to start..."
for i in $(seq 1 30); do
    if curl -sf http://localhost:5000/health > /dev/null 2>&1; then
        echo "    App is ready! (took ${i}s)"
        break
    fi
    if [ $i -eq 30 ]; then
        echo "ERROR: App failed to start within 60 seconds"
        kill $APP_PID 2>/dev/null
        exit 1
    fi
    sleep 2
done

# Set environment
export E2E_BASE_URL="http://localhost:5000"
export E2E_EMAIL="test.manager@shifty.test"
export E2E_PASSWORD="TestManager123!"

# Run tests
echo "[4/5] Running E2E tests..."
FAILURES=0

echo "  Running test_text_entry.py..."
if python test_text_entry.py; then
    echo "  ✓ test_text_entry.py passed"
else
    echo "  ✗ test_text_entry.py FAILED"
    FAILURES=$((FAILURES + 1))
fi

echo "  Running test_quick_entry.py..."
if python test_quick_entry.py; then
    echo "  ✓ test_quick_entry.py passed"
else
    echo "  ✗ test_quick_entry.py FAILED"
    FAILURES=$((FAILURES + 1))
fi

# Cleanup
echo "[5/5] Stopping app..."
kill $APP_PID 2>/dev/null
wait $APP_PID 2>/dev/null

# Report
echo ""
if [ $FAILURES -gt 0 ]; then
    echo "=== E2E TESTS FAILED ($FAILURES failures) ==="
    exit 1
fi
echo "=== ALL E2E TESTS PASSED ==="
