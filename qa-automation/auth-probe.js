// Auth probe: determine which seeded users actually exist on the running :5000 app.
// Run: node auth-probe.js
const { chromium } = require('@playwright/test');

const BASE = process.env.APP_URL || 'http://localhost:5000';

const CANDIDATES = [
  { label: 'Owner (Program.cs)',      email: 'admin@local',                password: 'admin123' },
  { label: 'Owner2 (QaSeed)',         email: 'owner2@test',                password: 'Test1234!' },
  { label: 'Director Alhut (QaSeed)', email: 'dir.alhut@test',             password: 'Test1234!' },
  { label: 'Mgr/Lead Alhut (QaSeed)', email: 'mgr.alhut.tz@test',          password: 'Test1234!' },
  { label: 'MoleculeAdmin (QaSeed)',  email: 'moladmin.oren@test',         password: 'Test1234!' },
  { label: 'AreaAdmin (QaSeed)',      email: 'areaadmin@test',             password: 'Test1234!' },
  { label: 'Assigner (QaSeed)',       email: 'assigner.oren@test',         password: 'Test1234!' },
  { label: 'Employee TZ (QaSeed)',    email: 'emp.tz.alhut@test',          password: 'Test1234!' },
  { label: 'Trainee (QaSeed)',        email: 'trainee.alhut@test',         password: 'Test1234!' },
  { label: 'NoGrants (QaSeed)',       email: 'nogrants@test',              password: 'Test1234!' },
  { label: 'Locked (QaSeed)',         email: 'locked@test',                password: 'Test1234!' },
  { label: 'Deactivated (QaSeed)',    email: 'deactivated@test',           password: 'Test1234!' },
  { label: 'E2E Owner (TestDataSeed)',email: 'test.owner@shifty.test',     password: 'TestOwner123!' },
];

async function tryLogin(page, c) {
  await page.goto(BASE + '/Auth/Login', { waitUntil: 'domcontentloaded' });
  await page.fill('input[name="Email"], input#Email', c.email);
  await page.fill('input[name="Password"], input#Password', c.password);
  let landed = '';
  try {
    await Promise.all([
      page.waitForURL(u => !u.toString().includes('/Auth/Login'), { timeout: 6000 }),
      page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
    ]);
    landed = page.url();
    return { ok: true, url: landed };
  } catch (e) {
    // Still on login — capture any validation/error text
    let err = '';
    try {
      err = (await page.locator('.validation-summary-errors, .alert-danger, [data-valmsg-summary], .field-validation-error, .text-danger').first().innerText({ timeout: 1000 })).trim();
    } catch {}
    return { ok: false, url: page.url(), err };
  }
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  console.log(`\n=== AUTH PROBE against ${BASE} ===\n`);
  for (const c of CANDIDATES) {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    try {
      const r = await tryLogin(page, c);
      const dest = r.url.replace(BASE, '') || '/';
      console.log(`${r.ok ? '✅ PASS' : '❌ FAIL'}  ${c.label.padEnd(28)} ${c.email.padEnd(28)} -> ${dest}${r.err ? '  [' + r.err.replace(/\s+/g,' ').slice(0,80) + ']' : ''}`);
    } catch (e) {
      console.log(`💥 ERR   ${c.label.padEnd(28)} ${c.email.padEnd(28)} -> ${String(e.message).slice(0,80)}`);
    }
    await ctx.close();
  }
  await browser.close();
  console.log('\n=== PROBE DONE ===\n');
})();
