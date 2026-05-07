// @ts-check
const { formPost } = require('./persona-helpers');

/**
 * Returns an ISO date string offsetDays from today (UTC midnight, local convention).
 * Used to construct unique vacation date ranges per test run so we don't collide
 * with prior runs' data (the test DB resets companies but NOT TimeOffRequests).
 *
 * @param {number} offsetDays
 */
function dateOffset(offsetDays) {
    const d = new Date();
    d.setUTCHours(0, 0, 0, 0);
    d.setUTCDate(d.getUTCDate() + offsetDays);
    return d.toISOString().slice(0, 10); // YYYY-MM-DD
}

/**
 * Create a pending TimeOffRequest by posting to the /My/Requests TimeOff handler
 * AS THE CURRENTLY LOGGED-IN USER. Caller is responsible for being logged in as
 * the requester. Returns the request ID extracted from the redirected page.
 *
 * @param {import('@playwright/test').Page} page
 * @param {{ startDate?: string, endDate?: string, type?: 'Vacation'|'After', reason?: string, approverId?: number }} opts
 * @returns {Promise<{ requestId: number, startDate: string, endDate: string }>}
 */
async function createPendingTimeOffRequest(page, opts = {}) {
    const startDate = opts.startDate || dateOffset(30);
    const endDate = opts.endDate || startDate;
    const type = opts.type || 'Vacation';
    const reason = opts.reason || `QA vacation ${Date.now()}`;

    // The form binds to TimeOffRequest.* (note: ApproverId is optional)
    const fields = {
        'TimeOffRequest.StartDate': startDate,
        'TimeOffRequest.EndDate': endDate,
        'TimeOffRequest.Type': type,
        'TimeOffRequest.Reason': reason,
        'TimeOffRequest.Private': 'false',
    };
    if (opts.approverId) fields['TimeOffRequest.ApproverId'] = String(opts.approverId);

    const result = await formPost(page, '/My/Requests?handler=TimeOff', fields);
    if (result.status >= 400 && result.status !== 302 && result.status !== 303) {
        throw new Error(`Failed to create TimeOffRequest: HTTP ${result.status}`);
    }

    // Reload the page and scrape the most recently created request ID for this user
    await page.goto('/My/Requests');
    await page.waitForLoadState('networkidle');

    const requestId = await page.evaluate((rsn) => {
        // /My/Requests renders each TimeOffRequest as <div class="request-item"> containing
        // the reason text and (for Pending requests) a CancelRequest form with
        // <input type="hidden" name="requestId" value="@request.Id" />.
        const items = Array.from(document.querySelectorAll('.request-item, .request-row, [data-request-id]'));
        for (const item of items) {
            const text = item.textContent || '';
            if (!text.includes(rsn)) continue;

            // Primary: hidden requestId input inside the cancel form
            const inp = item.querySelector('input[name="requestId"], input[name="RequestId"]');
            if (inp) {
                const v = inp.getAttribute('value');
                if (v && /^\d+$/.test(v)) return Number(v);
            }

            // Fallback: any [data-request-id] / [data-id] attribute
            const dataId = item.getAttribute('data-request-id') || item.getAttribute('data-id');
            if (dataId && /^\d+$/.test(dataId)) return Number(dataId);

            // Fallback: any name="id" hidden
            const idInp = item.querySelector('input[name="id"]');
            if (idInp) {
                const v = idInp.getAttribute('value');
                if (v && /^\d+$/.test(v)) return Number(v);
            }
        }
        return null;
    }, reason);

    if (!requestId) {
        throw new Error(`Created TimeOffRequest with reason "${reason}" but could not scrape its ID from /My/Requests`);
    }

    return { requestId, startDate, endDate };
}

/**
 * Owner-mediated: list all chore types. Used to pick a valid choreTypeId for tests.
 * Returns array of `{ id, title, moleculeId }`.
 *
 * @param {import('@playwright/test').Page} ownerPage
 */
async function listChoreTypes(ownerPage) {
    await ownerPage.goto('/Admin/Organization/ChoreTypes');
    await ownerPage.waitForLoadState('networkidle');
    return await ownerPage.evaluate(() => {
        const out = [];
        const rows = Array.from(document.querySelectorAll('tr[data-id], tr[data-chore-type-id]'));
        for (const r of rows) {
            const id = Number(r.getAttribute('data-id') || r.getAttribute('data-chore-type-id'));
            const title = (r.textContent || '').trim().slice(0, 80);
            const molAttr = r.getAttribute('data-molecule-id');
            const moleculeId = molAttr ? Number(molAttr) : null;
            if (Number.isFinite(id)) out.push({ id, title, moleculeId });
        }
        return out;
    });
}

/**
 * Owner-mediated: list shift types accessible from a given calendar URL. Used
 * to pick a valid shiftTypeId for happy-path AssignEmployee tests.
 *
 * @param {import('@playwright/test').Page} ownerPage
 * @param {string} calendarUrl  e.g. '/Calendar/Table?moleculeId=2&jobTypeId=1'
 */
async function listShiftTypesOnCalendar(ownerPage, calendarUrl) {
    await ownerPage.goto(calendarUrl);
    await ownerPage.waitForLoadState('networkidle');
    return await ownerPage.evaluate(() => {
        const out = [];
        // Calendar table renders shift types as rows with data-shift-type-id or similar
        const candidates = Array.from(document.querySelectorAll('[data-shift-type-id]'));
        for (const el of candidates) {
            const id = Number(el.getAttribute('data-shift-type-id'));
            const name = (el.textContent || '').trim().slice(0, 80);
            if (Number.isFinite(id)) out.push({ id, name });
        }
        // Deduplicate by id
        const seen = new Set();
        return out.filter(x => {
            if (seen.has(x.id)) return false;
            seen.add(x.id);
            return true;
        });
    });
}

module.exports = {
    dateOffset,
    createPendingTimeOffRequest,
    listChoreTypes,
    listShiftTypesOnCalendar,
};
