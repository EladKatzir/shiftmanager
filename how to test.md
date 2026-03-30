# ShiftManager — How to Run Tests

## Test Architecture Overview

The project has **three distinct test layers**:

1. **xUnit Unit Tests** (52 files) — fast, isolated, mock-based service tests
2. **xUnit Integration Tests** (6 files) — cross-component tests using in-memory DB
3. **Playwright E2E Tests** (62 files) — browser-based tests against a running app

---

## 1. .NET Tests (Unit + Integration)

Located in `ShiftManager.Tests/`. Uses **xUnit + Moq + FluentAssertions**.

### Run all .NET tests

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj
```

### Run with verbose output (see each test name)

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --verbosity normal
```

### Run only unit tests

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~UnitTests"
```

### Run only integration tests

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~IntegrationTests"
```

### Run a specific test class

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ShiftValidationTests"
```

### Run with code coverage

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --collect:"XPlat Code Coverage"
```

Coverage results go into `ShiftManager.Tests/TestResults/` as a Cobertura XML file.

---

## 2. Playwright E2E Tests

Located in `qa-automation/`. Requires a **running instance** of ShiftManager.

### First-time setup

```bash
cd qa-automation
npm install
npx playwright install chromium
```

### Prerequisites

You **must** have ShiftManager running locally first:

```bash
dotnet run
```

Then, in a separate terminal:

### Run all smoke/feature tests

```bash
cd qa-automation
npm test
```

### Run tests with a visible browser (headed mode)

```bash
npm run test:headed
```

### Run with Playwright debugger (step through tests)

```bash
npm run test:debug
```

### Run all production QA tests

```bash
npm run qa:production
```

### Run a specific production QA module

```bash
npm run qa:production:module -- "module-b"
```

### Run production QA + generate scorecard report

```bash
npm run qa:full
```

### View the HTML test report (after a run)

```bash
npm run report
```

### Run a single test file directly

```bash
npx playwright test tests/auth.spec.js
```

---

## 3. Run Everything

```bash
# Terminal 1: Run all .NET tests (no running app needed)
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj

# Terminal 2: Start the app, then run Playwright tests
dotnet run
# Wait for "Now listening on..." message

# Terminal 3: Run E2E tests
cd qa-automation
npm test && npm run qa:production
```

---

## Quick Reference

| What                | Command                                                        |
| ------------------- | -------------------------------------------------------------- |
| All .NET tests      | `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj`     |
| Unit tests only     | `dotnet test ... --filter "FullyQualifiedName~UnitTests"`      |
| Integration only    | `dotnet test ... --filter "FullyQualifiedName~IntegrationTests"` |
| One test class      | `dotnet test ... --filter "FullyQualifiedName~ClassName"`      |
| With coverage       | `dotnet test ... --collect:"XPlat Code Coverage"`              |
| Playwright all      | `npm test` (from `qa-automation/`)                             |
| Playwright headed   | `npm run test:headed`                                          |
| Playwright debug    | `npm run test:debug`                                           |
| Production QA       | `npm run qa:production`                                        |
| View report         | `npm run report`                                               |

## Clean Up Test Results

To delete all test output (artifacts, reports, videos, traces) without touching source code or baseline snapshots:

```bash
bash clean-test-results.sh
```

## Tips

- `dotnet test` automatically builds before testing — no separate `dotnet build` needed.
- `--filter` uses MSTest filter syntax: `FullyQualifiedName~Substring` for contains, `ClassName=Exact` for exact match. Combine with `&` (AND) and `|` (OR).
- Playwright's `--headed` flag lets you watch the browser execute each test step visually — great for debugging.
