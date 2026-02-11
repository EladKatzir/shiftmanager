const fs = require('fs');
const data = JSON.parse(fs.readFileSync('../ProductionReady/test-results.json', 'utf8'));

const results = [];

function walkSuite(suite, parentFile) {
  const file = suite.file || parentFile;
  for (const spec of suite.specs || []) {
    for (const test of spec.tests || []) {
      const lastResult = test.results[test.results.length - 1];
      let status;
      if (test.status === 'skipped') status = 'SKIP';
      else if (lastResult.status === 'passed') status = 'PASS';
      else if (lastResult.status === 'timedOut') status = 'TIMEOUT';
      else status = 'FAIL';

      const errMsg = status === 'PASS' ? '' :
        (lastResult.error && lastResult.error.message ? lastResult.error.message.substring(0, 200) : 'no error message');
      results.push({ file, test: spec.title, status, error: errMsg });
    }
  }
  for (const child of suite.suites || []) {
    walkSuite(child, file);
  }
}

for (const suite of data.suites) {
  walkSuite(suite);
}

// Per-file summary
const fileSummary = {};
for (const r of results) {
  if (!fileSummary[r.file]) fileSummary[r.file] = { pass: 0, fail: 0, skip: 0, timeout: 0 };
  if (r.status === 'PASS') fileSummary[r.file].pass++;
  else if (r.status === 'SKIP') fileSummary[r.file].skip++;
  else if (r.status === 'TIMEOUT') fileSummary[r.file].timeout++;
  else fileSummary[r.file].fail++;
}

console.log('=== PER-FILE SUMMARY (Run 1) ===');
for (const [f, c] of Object.entries(fileSummary).sort()) {
  const total = c.pass + c.fail + c.skip + c.timeout;
  const pct = total > 0 ? Math.round(c.pass / total * 100) : 0;
  console.log(`${f.padEnd(50)} P:${String(c.pass).padStart(3)} F:${String(c.fail).padStart(3)} S:${String(c.skip).padStart(3)} T:${String(c.timeout).padStart(3)}  Total:${total}  (${pct}%)`);
}

const tp = results.filter(r => r.status === 'PASS').length;
const tf = results.filter(r => r.status === 'FAIL').length;
const ts = results.filter(r => r.status === 'SKIP').length;
const tt = results.filter(r => r.status === 'TIMEOUT').length;
console.log('');
console.log(`GRAND TOTAL: P=${tp} F=${tf} S=${ts} T=${tt} (${tp+tf+ts+tt} total)`);
console.log(`Pass rate: ${Math.round(tp / (tp+tf+tt) * 100)}% (excluding skipped)`);
console.log('');

// Categorize failures
const failCategories = {};
for (const r of results) {
  if (r.status === 'FAIL' || r.status === 'TIMEOUT') {
    let cat;
    if (r.error.includes('Timeout') || r.error.includes('timeout') || r.status === 'TIMEOUT') cat = 'TIMEOUT';
    else if (r.error.includes('toBeVisible') || r.error.includes('not visible')) cat = 'ELEMENT_NOT_VISIBLE';
    else if (r.error.includes('toContainText') || r.error.includes('not contain')) cat = 'MISSING_TEXT';
    else if (r.error.includes('toBeGreaterThan') || r.error.includes('toBeLessThan')) cat = 'COUNT_MISMATCH';
    else if (r.error.includes('strict mode')) cat = 'STRICT_MODE';
    else cat = 'OTHER';

    if (!failCategories[cat]) failCategories[cat] = [];
    failCategories[cat].push({ file: r.file, test: r.test, error: r.error.substring(0, 120) });
  }
}

console.log('=== FAILURE CATEGORIES ===');
for (const [cat, items] of Object.entries(failCategories).sort((a, b) => b[1].length - a[1].length)) {
  console.log(`\n${cat} (${items.length} failures):`);
  for (const item of items.slice(0, 5)) {
    console.log(`  ${item.file}::${item.test}`);
    console.log(`    Error: ${item.error}`);
  }
  if (items.length > 5) console.log(`  ... and ${items.length - 5} more`);
}

// List all failures for full report
console.log('\n\n=== ALL FAILURES ===');
for (const r of results) {
  if (r.status === 'FAIL' || r.status === 'TIMEOUT') {
    console.log(`[${r.status}] ${r.file}::${r.test}`);
    console.log(`  ${r.error.substring(0, 180)}`);
  }
}
