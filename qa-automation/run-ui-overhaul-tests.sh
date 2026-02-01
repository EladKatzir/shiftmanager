#!/bin/bash
# Run UI Overhaul Playwright Tests

echo "==================================="
echo "ShiftManager UI Overhaul Test Suite"
echo "==================================="

# Check if app is running
if ! curl -s http://localhost:5000/Auth/Login > /dev/null; then
    echo "ERROR: ShiftManager is not running on http://localhost:5000"
    echo "Please start the application with: dotnet run"
    exit 1
fi

echo "Application is running. Starting tests..."

# Install dependencies if needed
if [ ! -d "node_modules" ]; then
    echo "Installing dependencies..."
    npm install
fi

# Run all UI overhaul tests
echo ""
echo "Running UI Overhaul tests..."
npx playwright test tests/ui-overhaul-*.spec.js \
    --reporter=html \
    --reporter=list \
    "$@"

echo ""
echo "Tests complete. Report available at: reports/playwright-report/index.html"
