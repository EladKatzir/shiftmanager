# Run UI Overhaul Playwright Tests
Write-Host "===================================" -ForegroundColor Cyan
Write-Host "ShiftManager UI Overhaul Test Suite" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan

# Check if app is running
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/Auth/Login" -UseBasicParsing -TimeoutSec 5
} catch {
    Write-Host "ERROR: ShiftManager is not running on http://localhost:5000" -ForegroundColor Red
    Write-Host "Please start the application with: dotnet run" -ForegroundColor Yellow
    exit 1
}

Write-Host "Application is running. Starting tests..." -ForegroundColor Green

# Install dependencies if needed
if (-not (Test-Path "node_modules")) {
    Write-Host "Installing dependencies..."
    npm install
}

# Run all UI overhaul tests
Write-Host ""
Write-Host "Running UI Overhaul tests..."
npx playwright test tests/ui-overhaul-*.spec.js `
    --reporter=html `
    --reporter=list `
    $args

Write-Host ""
Write-Host "Tests complete. Report available at: reports/playwright-report/index.html" -ForegroundColor Green
