// RBAC + stability route sweep.
// Logs in as each privilege tier, GETs every page route, records status + final URL,
// and auto-classifies: 5xx => STABILITY FAIL; under-privileged served Owner/Admin => ESCALATION FAIL.
// Run: node rbac-sweep.js   (writes reports/rbac-sweep.json)
const { chromium } = require('@playwright/test');
const fs = require('fs');

const BASE = process.env.APP_URL || 'http://localhost:5000';

const USERS = {
  Owner:        { email: 'admin@local',         password: 'admin123',   tier: 100 },
  Director:     { email: 'dir.alhut@test',      password: 'Test1234!',  tier: 80 },
  Manager:      { email: 'mgr.alhut.tz@test',   password: 'Test1234!',  tier: 60 },
  MoleculeAdmin:{ email: 'moladmin.oren@test',  password: 'Test1234!',  tier: 55 },
  AreaAdmin:    { email: 'areaadmin@test',      password: 'Test1234!',  tier: 58 },
  Assigner:     { email: 'assigner.oren@test',  password: 'Test1234!',  tier: 40 },
  Employee:     { email: 'emp.tz.alhut@test',   password: 'Test1234!',  tier: 20 },
  Trainee:      { email: 'trainee.alhut@test',  password: 'Test1234!',  tier: 10 },
  NoGrants:     { email: 'nogrants@test',        password: 'Test1234!',  tier: 5 },
};

// required tier per route prefix (longest-prefix wins)
const TIERS = [
  ['/Owner',  100],
  ['/Admin',  60],   // manager+ (some sub-pages gate further by grant)
  ['/Director',80],
  ['/Dev',    100],
  ['/GriffinDiagnostic', 100],
  ['/Diagnostic', 100],
];
function requiredTier(route){
  let best = 0;
  for (const [p,t] of TIERS) if (route.startsWith(p) && p.length > 0) best = Math.max(best, route.startsWith(p)?t:0);
  // longest prefix match
  let match = 0, mt = 0;
  for (const [p,t] of TIERS){ if (route.startsWith(p) && p.length>match){ match=p.length; mt=t; } }
  return mt; // 0 = any authenticated user
}

// Curated GET-able page routes (excludes Api POST handlers, components, Logout).
const ROUTES = [
  '/', '/Home', '/AccessDenied', '/Error', '/StatusCode',
  // My / personal
  '/My', '/My/Profile', '/My/Settings', '/My/Requests', '/My/NotificationCenter',
  '/My/ApiKeys', '/My/Help', '/My/HelpCalendar', '/My/HelpGettingStarted', '/My/HelpRoles',
  '/My/HelpAdmin', '/My/Onboarding',
  // Calendars
  '/Calendar', '/Calendar/Shifts', '/Calendar/Chores', '/Calendar/OnCall', '/Calendar/Overview',
  '/Calendar/Day', '/Calendar/Week', '/Calendar/Month', '/Calendar/Table',
  '/Calendar/ManageDistributionLists', '/Chores/Calendar',
  // Social / team
  '/Friends', '/MyTeam', '/Schedule', '/Game/Leaderboard',
  // Requests / assignments
  '/Requests', '/Requests/Swaps/Create', '/Assignments/Manage',
  // Public
  '/Public/Chores', '/Public/OnDuty', '/Public/Feedback',
  // Director
  '/Director/CompanyFilter', '/Director/NotificationHub', '/Director/ViewAsMode',
  // Admin
  '/Admin', '/Admin/Users', '/Admin/Companies', '/Admin/Analytics', '/Admin/Announcements',
  '/Admin/AuditLog', '/Admin/Config', '/Admin/Directors', '/Admin/EditProfile',
  '/Admin/DutyRotation', '/Admin/HomeTypes', '/Admin/SetupTasks',
  '/Admin/Organization', '/Admin/Organization/Areas', '/Admin/Organization/Molecules',
  '/Admin/Organization/Projects', '/Admin/Organization/Departments', '/Admin/Organization/JobTypes',
  '/Admin/Organization/ChoreTypes', '/Admin/Organization/DutyTypes', '/Admin/Organization/Stores',
  '/Admin/Organization/ShiftGroupings', '/Admin/Organization/Hierarchy', '/Admin/Organization/Grants',
  '/Admin/Organization/Roles', '/Admin/Organization/AreaPalette', '/Admin/Settings',
  '/Admin/Settings/ApprovalRules', '/Admin/Molecules/ApprovalSettings',
  // Owner
  '/Owner', '/Owner/Hub', '/Owner/Hub/Grants', '/Owner/Hub/PermissionSimulator',
  '/Owner/Hub/RoleTemplates', '/Owner/Hub/SeedData', '/Owner/Hub/AuditSearch',
  '/Owner/AreaConfig', '/Owner/Backup', '/Owner/Blueprints', '/Owner/DataLifecycle',
  '/Owner/DatabaseConsole', '/Owner/EmailConfig', '/Owner/EmailTemplates', '/Owner/FeatureFlags',
  '/Owner/GameConfig', '/Owner/GriffinConfig', '/Owner/LanguageManagement', '/Owner/LockedUsers',
  '/Owner/MasterPrograms', '/Owner/Permissions', '/Owner/Programs', '/Owner/SelectCompany',
  '/Owner/SystemHealth', '/Owner/Telemetry',
  // Diagnostics
  '/Diagnostic', '/GriffinDiagnostic', '/Dev/FeedbackPreview',
];

async function waitForApp(page){
  // Tolerate transient restarts from the concurrent peer session rebuilding.
  for (let i=0;i<24;i++){
    try { const r = await page.goto(BASE + '/Auth/Login', { waitUntil:'domcontentloaded', timeout:6000 }); if (r) return true; }
    catch { await page.waitForTimeout(5000); }
  }
  return false;
}

async function login(ctx, u){
  const page = await ctx.newPage();
  await waitForApp(page);
  await page.fill('input[name="Email"], input#Email', u.email);
  await page.fill('input[name="Password"], input#Password', u.password);
  await Promise.all([
    page.waitForURL(x => !x.toString().includes('/Auth/Login'), { timeout: 8000 }).catch(()=>{}),
    page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
  ]);
  await page.close();
}

function isDenied(finalUrl, status){
  return status === 403 || status === 401 ||
    /\/AccessDenied/i.test(finalUrl) || /\/Auth\/Login/i.test(finalUrl) || /\/Error/i.test(finalUrl);
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const results = {}; const issues = [];
  for (const [roleName, u] of Object.entries(USERS)) {
    const ctx = await browser.newContext({ baseURL: BASE });
    await login(ctx, u);
    const page = await ctx.newPage();
    results[roleName] = {};
    for (const route of ROUTES) {
      let status = 0, finalUrl = '', err = '';
      for (let attempt=0; attempt<3; attempt++){
        try {
          const resp = await page.goto(BASE + route, { waitUntil: 'domcontentloaded', timeout: 15000 });
          status = resp ? resp.status() : 0;
          finalUrl = page.url();
          err = '';
          break;
        } catch (e) {
          err = String(e.message).slice(0,60); finalUrl = page.url();
          if (/ERR_CONNECTION_REFUSED|ECONNREFUSED|ERR_CONNECTION_RESET/.test(err)) { await page.waitForTimeout(6000); continue; }
          break;
        }
      }
      const req = requiredTier(route);
      const denied = isDenied(finalUrl, status);
      let verdict = 'PASS';
      if (status >= 500) { verdict = 'FAIL-5XX'; issues.push(`[${roleName}] ${route} -> HTTP ${status} (server error)`); }
      else if (err) { verdict = 'ERR'; issues.push(`[${roleName}] ${route} -> nav error: ${err}`); }
      else if (req > u.tier) {
        // under-privileged: MUST be denied
        if (!denied) { verdict = 'FAIL-ESCALATION'; issues.push(`[${roleName}] ${route} -> SERVED (HTTP ${status}, ${finalUrl.replace(BASE,'')}) but required tier ${req} > user tier ${u.tier}`); }
      } else {
        // authorized: should be served, not 5xx; denial is a WARN (possible over-restriction / grant gate)
        if (denied && route !== '/AccessDenied' && route !== '/Error') verdict = 'WARN-DENIED';
      }
      results[roleName][route] = { status, finalUrl: finalUrl.replace(BASE,''), verdict, req };
    }
    await ctx.close();
    process.stdout.write(`  done: ${roleName}\n`);
    // Incremental save: survive a hard app crash without losing completed roles.
    fs.mkdirSync('reports', { recursive: true });
    fs.writeFileSync('reports/rbac-sweep.json', JSON.stringify({ results, issues }, null, 2));
  }
  await browser.close();

  fs.mkdirSync('reports', { recursive: true });
  fs.writeFileSync('reports/rbac-sweep.json', JSON.stringify({ results, issues }, null, 2));

  // Console summary: only non-PASS rows
  console.log('\n=== RBAC SWEEP: NON-PASS RESULTS ===\n');
  let counts = {};
  for (const [role, routes] of Object.entries(results)) {
    for (const [route, r] of Object.entries(routes)) {
      counts[r.verdict] = (counts[r.verdict]||0)+1;
      if (r.verdict !== 'PASS') console.log(`${r.verdict.padEnd(16)} ${role.padEnd(13)} ${route.padEnd(40)} HTTP ${r.status} -> ${r.finalUrl}`);
    }
  }
  console.log('\n=== COUNTS ===', JSON.stringify(counts));
  console.log(`\n=== CRITICAL ISSUES (${issues.length}) ===`);
  issues.forEach(i => console.log('  • ' + i));
})();
