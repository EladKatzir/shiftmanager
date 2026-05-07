// @ts-check
const { expect } = require('@playwright/test');

/**
 * Persona helpers for organizational-boundary QA.
 *
 * "shikma" and "oren" are MOLECULES, not companies. Pie/Tao live under Shikma;
 * Tzafona/Hir/Hitazmut/Element/Hamasa live under Oren. The personas here map the
 * investor-call test names to the actual seeded test users in QaTestUserSeed.cs.
 *
 * Password for every persona is "Test1234!".
 */
const PERSONAS = {
    ShikmaAssigner:   { email: 'assigner.pie@test',   pwd: 'Test1234!', company: 'Pie',     molecule: 'Shikma',   roleKey: 'Assigner'   },
    ShikmaBRDirector: { email: 'deptlead.pie@test',   pwd: 'Test1234!', company: 'Pie',     molecule: 'Shikma',   roleKey: 'BRDirector' },
    OrenAlhutLead:    { email: 'mgr.alhut.tz@test',   pwd: 'Test1234!', company: 'Tzafona', molecule: 'Oren',     roleKey: 'Lead', jobType: 'Alhut' },
    ShikmaEmployee:   { email: 'emp.tech.pie@test',   pwd: 'Test1234!', company: 'Pie',     molecule: 'Shikma',   roleKey: 'Employee'   },
    OrenAlhutEmp:     { email: 'emp.tz.alhut@test',   pwd: 'Test1234!', company: 'Tzafona', molecule: 'Oren',     roleKey: 'Employee', jobType: 'Alhut' },
    OrenElementEmp:   { email: 'emp.elem.alhut@test', pwd: 'Test1234!', company: 'Element', molecule: 'Oren',     roleKey: 'Employee', jobType: 'Alhut' },
    OrenHitazmutEmp:  { email: 'emp.hit.alhut@test',  pwd: 'Test1234!', company: 'Hitazmut',molecule: 'Oren',     roleKey: 'Employee', jobType: 'Alhut' },
    Owner:            { email: 'admin@local',         pwd: 'admin123',  company: '*',       molecule: '*',        roleKey: 'Owner'      },
};

/**
 * Log in as a named persona via the local credentials form (NOT the Griffin/ADFS button).
 * Verifies the post-login URL is no longer /Auth/Login.
 *
 * @param {import('@playwright/test').Page} page
 * @param {keyof typeof PERSONAS} personaKey
 */
async function loginAsPersona(page, personaKey) {
    const persona = PERSONAS[personaKey];
    if (!persona) throw new Error(`Unknown persona: ${personaKey}. Valid: ${Object.keys(PERSONAS).join(', ')}`);

    await page.goto('/Auth/Login');
    await page.fill('input#Email, input[name="Email"]', persona.email);
    await page.fill('input#Password, input[name="Password"]', persona.pwd);

    await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 10000 }),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
    ]);

    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    return persona;
}

/**
 * Log out the current user. Best-effort; ignores errors so tests can chain it.
 *
 * @param {import('@playwright/test').Page} page
 */
async function logout(page) {
    try {
        await page.goto('/Auth/Logout');
    } catch {
        // Some apps use POST for logout; try the alternative
        try { await page.request.post('/Auth/Logout'); } catch { /* swallow */ }
    }
    // Also clear cookies as a hard reset between persona switches
    await page.context().clearCookies();
}

/**
 * Extract the Razor Pages anti-forgery token from the currently loaded page.
 * Falls back to fetching the login page if no token is present (rare).
 *
 * @param {import('@playwright/test').Page} page
 * @returns {Promise<string>}
 */
async function extractCsrfToken(page) {
    let token = await page.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value').catch(() => null);
    if (!token) {
        // Cold path: pull a fresh token from any page that has a form
        await page.goto('/My/Requests');
        token = await page.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
    }
    if (!token) throw new Error('Could not extract __RequestVerificationToken from any page');
    return token;
}

/**
 * Fire a JSON POST against a Razor Pages handler. The CSRF header
 * `RequestVerificationToken` is the canonical header name expected by
 * ASP.NET Core's antiforgery middleware. Endpoints decorated with
 * `[IgnoreAntiforgeryToken]` (e.g. QuickAddChore, QuickAddOnDuty) will
 * accept the request even without the header — we send it anyway for
 * uniformity.
 *
 * Returns `{ status, body }` where body is parsed JSON if possible, else string.
 *
 * @param {import('@playwright/test').Page} page
 * @param {string} url
 * @param {object} jsonBody
 */
async function jsonPost(page, url, jsonBody) {
    const token = await extractCsrfToken(page).catch(() => '');
    const resp = await page.request.post(url, {
        data: jsonBody,
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token,
            'X-Requested-With': 'XMLHttpRequest',
        },
        failOnStatusCode: false,
    });
    const status = resp.status();
    let body;
    try { body = await resp.json(); }
    catch { body = await resp.text(); }
    return { status, body };
}

/**
 * Fire a form-encoded POST (Razor Pages handler convention with `?handler=Foo`).
 * Used for vacation approve/decline, which use TempData/Page() rendering rather
 * than JSON envelopes.
 *
 * @param {import('@playwright/test').Page} page
 * @param {string} url   e.g. '/Requests?handler=ApproveTimeOff'
 * @param {Record<string, string|number>} formFields
 */
async function formPost(page, url, formFields) {
    const token = await extractCsrfToken(page);
    const formData = { __RequestVerificationToken: token, ...formFields };
    const resp = await page.request.post(url, {
        form: formData,
        failOnStatusCode: false,
    });
    return { status: resp.status(), location: resp.headers()['location'] || null, body: await resp.text() };
}

/**
 * Owner-mediated lookup: given an authenticated Owner page, return the integer
 * user ID for the given email by scraping the /Admin/Users page. We use Owner
 * specifically because they bypass tenant filters and can see victims across
 * companies/molecules — exactly what attacker tests need.
 *
 * Caches results on the page object to avoid repeated scrapes.
 *
 * @param {import('@playwright/test').Page} ownerPage  page authenticated as Owner
 * @param {string} email
 * @returns {Promise<number>}
 */
async function findUserIdAsOwner(ownerPage, email) {
    // @ts-ignore — runtime cache attached to page
    if (!ownerPage.__userIdCache) ownerPage.__userIdCache = new Map();
    // @ts-ignore
    const cache = ownerPage.__userIdCache;
    if (cache.has(email)) return cache.get(email);

    // /Admin/Users paginates at PageSize=50; walk pages until found or no more.
    // The page exposes ?CurrentPage as [BindProperty(SupportsGet)].
    let pageNum = 1;
    const MAX_PAGES = 20; // 1000 users — well above any realistic test set
    let id = null;

    for (; pageNum <= MAX_PAGES && id == null; pageNum++) {
        await ownerPage.goto(`/Admin/Users?CurrentPage=${pageNum}`);
        await ownerPage.waitForLoadState('networkidle');

        const result = await ownerPage.evaluate(scrapeIdAndStats, email);
        id = result.id;

        // Termination: if there are 0 rows on this page OR fewer than the page size, stop after.
        if (id == null && result.rowCount < 50) break;
    }

    if (id != null) {
        cache.set(email, id);
        return id;
    }

    throw new Error(`Could not find user ID for email '${email}' after walking ${pageNum - 1} pages of /Admin/Users (Owner login)`);
}

/**
 * Browser-context evaluator: searches the current /Admin/Users page for an email
 * and extracts its user ID. Returns `{ id, rowCount }` so the caller can decide
 * whether to fetch the next page.
 */
function scrapeIdAndStats(targetEmail) {
    const rows = Array.from(document.querySelectorAll('#usersTable tbody tr, tbody tr'));
    const rowCount = rows.length;
    for (const row of rows) {
        const text = row.textContent || '';
        if (!text.includes(targetEmail)) continue;

        // 1. <td data-user-id="123"> — primary pattern in /Admin/Users.cshtml editable cells
        const cellWithId = row.querySelector('[data-user-id]');
        if (cellWithId) {
            const v = cellWithId.getAttribute('data-user-id');
            if (v && /^\d+$/.test(v)) return { id: Number(v), rowCount };
        }

        // 2. <input name="id" value="123"> inside child forms (asp-page-handler="JobType" etc.)
        const inputId = row.querySelector('input[name="id"]');
        if (inputId) {
            const v = inputId.getAttribute('value');
            if (v && /^\d+$/.test(v)) return { id: Number(v), rowCount };
        }

        // 3. Row data attribute fallback
        const rowDataId = row.getAttribute('data-user-id') || row.getAttribute('data-id');
        if (rowDataId && /^\d+$/.test(rowDataId)) return { id: Number(rowDataId), rowCount };

        // 4. Anchor / form action with ?id=N or /N path segment
        const candidates = Array.from(row.querySelectorAll('a[href], form[action]'));
        for (const el of candidates) {
            const href = el.getAttribute('href') || el.getAttribute('action') || '';
            const m = href.match(/[?&]id=(\d+)/i) || href.match(/\/(\d+)(?:\/|$)/);
            if (m) return { id: Number(m[1]), rowCount };
        }
    }
    return { id: null, rowCount };
}

/**
 * Owner-mediated lookup: integer molecule ID by name.
 *
 * @param {import('@playwright/test').Page} ownerPage
 * @param {string} moleculeName
 * @returns {Promise<number>}
 */
async function findMoleculeIdAsOwner(ownerPage, moleculeName) {
    // @ts-ignore
    if (!ownerPage.__molIdCache) ownerPage.__molIdCache = new Map();
    // @ts-ignore
    const cache = ownerPage.__molIdCache;
    if (cache.has(moleculeName)) return cache.get(moleculeName);

    // Try the Organization tree page first; fall back to a generic admin page
    const candidates = ['/Admin/Organization', '/Owner/Hub', '/Admin/Organization/Molecules'];
    for (const url of candidates) {
        try {
            const resp = await ownerPage.goto(url);
            if (resp && resp.status() === 200) {
                await ownerPage.waitForLoadState('networkidle');
                const id = await ownerPage.evaluate((name) => {
                    const elements = Array.from(document.querySelectorAll('[data-molecule-id], [data-mol-id], [data-id]'));
                    for (const el of elements) {
                        const text = el.textContent || '';
                        if (text.toLowerCase().includes(name.toLowerCase())) {
                            const id = el.getAttribute('data-molecule-id') || el.getAttribute('data-mol-id') || el.getAttribute('data-id');
                            if (id && /^\d+$/.test(id)) return Number(id);
                        }
                    }
                    return null;
                }, moleculeName);
                if (id != null) { cache.set(moleculeName, id); return id; }
            }
        } catch { /* try next */ }
    }
    throw new Error(`Could not resolve molecule ID for '${moleculeName}' via Owner UI`);
}

/**
 * Convenience: owner login + lookup of all relevant test user IDs in one shot.
 * Returns a map { email -> userId }.
 *
 * @param {import('@playwright/test').BrowserContext} ownerContext
 * @param {string[]} emails
 */
async function lookupUserIds(ownerContext, emails) {
    const page = await ownerContext.newPage();
    try {
        await loginAsPersona(page, 'Owner');
        const map = {};
        for (const email of emails) {
            map[email] = await findUserIdAsOwner(page, email);
        }
        return map;
    } finally {
        await page.close();
    }
}

module.exports = {
    PERSONAS,
    loginAsPersona,
    logout,
    extractCsrfToken,
    jsonPost,
    formPost,
    findUserIdAsOwner,
    findMoleculeIdAsOwner,
    lookupUserIds,
};
