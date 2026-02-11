const fs = require('fs');
let raw = fs.readFileSync('../ProductionReady/test-results-run2.json', 'utf8');

// Try parsing incrementally, handling potential corruption
let data;
try {
  // First try: naive brace matching for outermost object
  let depth = 0;
  let inString = false;
  let escape = false;
  let end = 0;
  for (let i = 0; i < raw.length; i++) {
    const c = raw[i];
    if (escape) { escape = false; continue; }
    if (c === '\\' && inString) { escape = true; continue; }
    if (c === '"') { inString = !inString; continue; }
    if (inString) continue;
    if (c === '{') depth++;
    else if (c === '}') {
      depth--;
      if (depth === 0) { end = i + 1; break; }
    }
  }
  data = JSON.parse(raw.substring(0, end));
} catch(e) {
  console.log('JSON parse error:', e.message);
  process.exit(1);
}

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
      results.push({ file, test: spec.title, status });
    }
  }
  for (const child of suite.suites || []) { walkSuite(child, file); }
}
for (const suite of data.suites) { walkSuite(suite); }

const tp = results.filter(r => r.status === 'PASS').length;
const tf = results.filter(r => r.status === 'FAIL').length;
const ts = results.filter(r => r.status === 'SKIP').length;
const tt = results.filter(r => r.status === 'TIMEOUT').length;

console.log('=== RUN 2 RESULTS (post bug-fix) ===');
console.log('PASS:', tp, '  FAIL:', tf, '  SKIP:', ts, '  TIMEOUT:', tt, '  TOTAL:', tp+tf+ts+tt);
console.log('Pass rate:', Math.round(tp / (tp+tf+tt) * 100) + '% (excluding skipped)');
console.log('');

const fileSummary = {};
for (const r of results) {
  if (!fileSummary[r.file]) fileSummary[r.file] = { pass: 0, fail: 0, skip: 0, timeout: 0 };
  if (r.status === 'PASS') fileSummary[r.file].pass++;
  else if (r.status === 'SKIP') fileSummary[r.file].skip++;
  else if (r.status === 'TIMEOUT') fileSummary[r.file].timeout++;
  else fileSummary[r.file].fail++;
}

console.log('=== PER-FILE SUMMARY ===');
for (const [f, c] of Object.entries(fileSummary).sort()) {
  const total = c.pass + c.fail + c.skip + c.timeout;
  const pct = total > 0 ? Math.round(c.pass / total * 100) : 0;
  const short = f.replace(/.*tests\/production-qa\//, '');
  console.log(short.padEnd(45) + ' P:' + String(c.pass).padStart(3) + ' F:' + String(c.fail).padStart(3) + ' S:' + String(c.skip).padStart(3) + ' T:' + String(c.timeout).padStart(3) + '  (' + pct + '%)');
}

console.log('');
console.log('=== COMPARISON WITH RUN 1 ===');
console.log('Run 1: 220 pass, 187 fail, 24 skip (54% pass rate)');
console.log('Run 2:', tp, 'pass,', tf, 'fail,', ts, 'skip (' + Math.round(tp / (tp+tf+tt) * 100) + '% pass rate)');
console.log('Delta: ' + (tp > 220 ? '+' : '') + (tp - 220) + ' pass, ' + (tf < 187 ? '' : '+') + (tf - 187) + ' fail');
