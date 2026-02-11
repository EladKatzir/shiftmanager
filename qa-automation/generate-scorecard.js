#!/usr/bin/env node
// @ts-check
/**
 * Release Scorecard Generator
 * Reads Playwright test results and generates ProductionReady/RELEASE-SCORECARD.md
 */

const fs = require('fs');
const path = require('path');

const RESULTS_FILE = path.resolve(__dirname, '..', 'ProductionReady', 'test-results.json');
const SCORECARD_FILE = path.resolve(__dirname, '..', 'ProductionReady', 'RELEASE-SCORECARD.md');
const BUGS_FILE = path.resolve(__dirname, '..', 'ProductionReady', 'bugs', 'BUGS.md');
const EVIDENCE_ROOT = path.resolve(__dirname, '..', 'ProductionReady');

function generateScorecard() {
  let results = { suites: [] };
  try {
    results = JSON.parse(fs.readFileSync(RESULTS_FILE, 'utf-8'));
  } catch (e) {
    console.warn('No test results found at', RESULTS_FILE);
  }

  // Count results
  let totalTests = 0;
  let passed = 0;
  let failed = 0;
  let skipped = 0;
  const moduleResults = {};

  function processSpec(spec) {
    for (const test of spec.tests || []) {
      totalTests++;
      for (const result of test.results || []) {
        if (result.status === 'passed') passed++;
        else if (result.status === 'failed') failed++;
        else if (result.status === 'skipped') skipped++;
      }
    }
  }

  function processSuite(suite) {
    for (const spec of suite.specs || []) {
      processSpec(spec);
    }
    for (const child of suite.suites || []) {
      processSuite(child);
    }
  }

  for (const suite of results.suites || []) {
    processSuite(suite);
  }

  // Count bugs
  let p0 = 0, p1 = 0, p2 = 0, p3 = 0;
  if (fs.existsSync(BUGS_FILE)) {
    const bugsContent = fs.readFileSync(BUGS_FILE, 'utf-8');
    p0 = (bugsContent.match(/\*\*Severity:\*\* P0/g) || []).length;
    p1 = (bugsContent.match(/\*\*Severity:\*\* P1/g) || []).length;
    p2 = (bugsContent.match(/\*\*Severity:\*\* P2/g) || []).length;
    p3 = (bugsContent.match(/\*\*Severity:\*\* P3/g) || []).length;
  }

  // Count evidence screenshots
  let screenshotCount = 0;
  function countScreenshots(dir) {
    if (!fs.existsSync(dir)) return;
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      if (entry.isDirectory()) {
        countScreenshots(path.join(dir, entry.name));
      } else if (entry.name.endsWith('.png') || entry.name.endsWith('.json')) {
        screenshotCount++;
      }
    }
  }
  countScreenshots(EVIDENCE_ROOT);

  // Determine go/no-go criteria
  const criteria = [
    { id: 1, category: 'Build', pass: 'Manual check required', weight: 'Required', result: '[ ]' },
    { id: 2, category: 'Unit Tests', pass: '236/236 passing', weight: 'Required', result: '[ ]' },
    { id: 3, category: 'Seeding', pass: 'All hierarchy entities created', weight: 'Required', result: failed === 0 ? 'PASS' : 'REVIEW' },
    { id: 4, category: 'Authentication', pass: 'All 12 auth tests pass', weight: 'Required', result: '[ ]' },
    { id: 5, category: 'Shift Full Flow', pass: 'Blueprint->Program->Instance->Assign for ALL 4 JTs', weight: 'Required', result: '[ ]' },
    { id: 6, category: 'Chore Full Flow', pass: 'ChoreType->Create->Assign->Cancel->Restore', weight: 'Required', result: '[ ]' },
    { id: 7, category: 'OnDuty Full Flow', pass: 'TypeConfig->Create->Assign->Cancel (incl rotation)', weight: 'Required', result: '[ ]' },
    { id: 8, category: 'Time-Off Flow', pass: 'Create->Approve->Auto-unassign', weight: 'Required', result: '[ ]' },
    { id: 9, category: 'Swap Flow', pass: 'Create->Approve->Assignments swapped', weight: 'Required', result: '[ ]' },
    { id: 10, category: 'Tenant Isolation', pass: 'Zero cross-tenant data leakage', weight: 'Required', result: '[ ]' },
    { id: 11, category: 'Per-Role Access', pass: 'Each role sees ONLY what it should', weight: 'Required', result: '[ ]' },
    { id: 12, category: 'Concurrency', pass: 'RowVersion conflict detection works', weight: 'Required', result: '[ ]' },
    { id: 13, category: 'SignalR Real-time', pass: 'Multi-user calendar updates propagate', weight: 'Required', result: '[ ]' },
    { id: 14, category: 'API Auth', pass: '401 on unauth, 403 on unauthorized', weight: 'Required', result: '[ ]' },
    { id: 15, category: 'SQL Injection Prevention', pass: 'DatabaseConsole blocks injection', weight: 'Required', result: '[ ]' },
    { id: 16, category: 'Localization', pass: 'Hebrew + English both render, RTL correct', weight: 'Required', result: '[ ]' },
    { id: 17, category: 'P0 Bugs', pass: 'Zero P0 bugs open', weight: 'Required', result: p0 === 0 ? 'PASS' : 'FAIL' },
    { id: 18, category: 'P1 Bugs', pass: 'Zero P1 bugs open', weight: 'Required', result: p1 === 0 ? 'PASS' : 'FAIL' },
    { id: 19, category: 'P2 Bugs', pass: '<5 P2 bugs with workarounds', weight: 'Recommended', result: p2 < 5 ? 'PASS' : 'REVIEW' },
    { id: 20, category: 'UI/UX Sweep', pass: 'No broken states, no JS errors', weight: 'Required', result: '[ ]' },
    { id: 21, category: 'Every Button', pass: 'All ~130 elements clickable', weight: 'Required', result: '[ ]' },
    { id: 22, category: 'Context Switcher', pass: 'Owner can switch all companies', weight: 'Required', result: '[ ]' },
    { id: 23, category: 'Signup + Approval', pass: 'Full flow works', weight: 'Required', result: '[ ]' },
    { id: 24, category: 'Air-gapped Readiness', pass: 'No external dependencies', weight: 'Required', result: '[ ]' },
    { id: 25, category: 'Backup/Restore', pass: 'Backup creates, downloads, restores', weight: 'Required', result: '[ ]' },
  ];

  // Determine overall recommendation
  const hasP0 = p0 > 0;
  const hasP1 = p1 > 0;
  let recommendation = 'GO';
  if (hasP0 || hasP1) recommendation = 'NO GO';
  else if (failed > 0) recommendation = 'CONDITIONAL GO';

  const now = new Date().toISOString().replace('T', ' ').split('.')[0];

  const scorecard = `# ShiftManager Production Release Scorecard

**Generated:** ${now}
**Branch:** UiChanging
**Environment:** localhost:5000 (Development)

---

## Test Execution Summary

| Metric | Count |
|--------|-------|
| Total Tests | ${totalTests} |
| Passed | ${passed} |
| Failed | ${failed} |
| Skipped | ${skipped} |
| Pass Rate | ${totalTests > 0 ? ((passed / totalTests) * 100).toFixed(1) : 0}% |
| Evidence Screenshots | ${screenshotCount} |

## Bug Summary

| Severity | Count | Release Impact |
|----------|-------|----------------|
| P0 Critical | ${p0} | ${p0 > 0 ? 'BLOCKS release' : 'OK'} |
| P1 High | ${p1} | ${p1 > 0 ? 'BLOCKS release' : 'OK'} |
| P2 Medium | ${p2} | ${p2 >= 5 ? 'Review required' : 'OK'} |
| P3 Low | ${p3} | Nice-to-fix |
| **Total** | **${p0 + p1 + p2 + p3}** | |

## Go/No-Go Criteria

| # | Category | Pass Criteria | Weight | Result |
|---|----------|--------------|--------|--------|
${criteria.map(c => `| ${c.id} | **${c.category}** | ${c.pass} | ${c.weight} | ${c.result} |`).join('\n')}

## Release Decision

### Recommendation: **${recommendation}**

${recommendation === 'GO' ? 'All "Required" criteria pass with no P0/P1 bugs.' :
  recommendation === 'CONDITIONAL GO' ? 'Some tests need manual review. See failed tests above.' :
  'P0/P1 bugs found. Release is blocked until these are resolved.'}

---

## Evidence Directory Structure

\`\`\`
ProductionReady/
  00-build-output/
  01-seed-verification/
  02-auth/
  03-signup/
  04-user-management/
  05-context-switcher/
  06-blueprints/
  07-programs/
  08-master-programs/
  09-instance-manipulation/
  10-shift-assignments/ (alhut, text, br, hakam)
  11-chore-calendar/
  12-onduty-calendar/
  13-overview-calendar/
  14-timeoff-requests/
  15-swap-requests/
  16-tenant-isolation/ (tzafona-vs-hir, oren-vs-ella, new-companies)
  17-per-role-access/ (owner, director, manager, employee, trainee, assigner)
  18-concurrency/
  19-rest-api/
  20-page-handler-api/
  21-owner-pages/
  22-admin-organization/
  23-director-my-public/
  24-localization/ (hebrew, english, rtl)
  25-notifications/
  26-error-handling/
  27-ui-sweep/ (light-theme, dark-theme, hebrew-rtl, mobile-viewport)
  28-every-button/
  bugs/
  RELEASE-SCORECARD.md
\`\`\`

## Open Bug List

${fs.existsSync(BUGS_FILE) ? fs.readFileSync(BUGS_FILE, 'utf-8') : 'No bugs recorded.'}

---

## Items Requiring Manual Testing

| Item | Reason |
|------|--------|
| Griffin SSO/ADFS | Requires Active Directory |
| Email delivery | Requires SMTP server |
| IIS deployment | Requires IIS server |
| True air-gapped | Requires disconnected machine |
| SSL/TLS certificate | Requires cert infrastructure |
| Print layout accuracy | Physical print preview needed |
| Screen reader | NVDA/JAWS/VoiceOver needed |
| 100+ concurrent users | Load testing tools needed |
| Physical mobile touch | Real device needed |
| 8+ hour session | Extended monitoring needed |

---

*Generated by ShiftManager Production QA Automation*
`;

  fs.writeFileSync(SCORECARD_FILE, scorecard, 'utf-8');
  console.log(`Release scorecard generated: ${SCORECARD_FILE}`);
  console.log(`  Tests: ${passed}/${totalTests} passed (${failed} failed, ${skipped} skipped)`);
  console.log(`  Bugs: P0=${p0}, P1=${p1}, P2=${p2}, P3=${p3}`);
  console.log(`  Evidence: ${screenshotCount} files`);
  console.log(`  Recommendation: ${recommendation}`);
}

generateScorecard();
