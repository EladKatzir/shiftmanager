// baseline.spec.js — Calendar sticky-headers visual regression baselines
//
// Implements spec §13.1 "Automated" baseline matrix:
//   - 4 calendars  (Shifts, OnCall, Chores, Overview)
//   - 2 themes     (light, dark)
//   - 2 languages  (en, he)
//   - 4 scroll states (rest, scrolled-x, scrolled-y, scrolled-both)
// = 64 baseline images at viewport 1366×768
//
// Plus the 414×896 mobile overflow-sheet baseline.
//
// Baselines in this directory were captured 2026-05-30 via interactive
// Playwright MCP session (Task 20). The manual capture proved:
//   - Sticky toolbar engages on all 4 calendars
//   - Mode token renders correctly in EN + HE for all 4 row-mode values
//   - aria-rowcount includes thead (Task 12 +1 fix verified at runtime)
//   - Task 4's flex-column chain (position:relative; overflow:auto; min-height:0)
//     is computed correctly on .excel-calendar
//   - Task 10's IntersectionObserver axis-discrimination (rootMargin 100%
//     orthogonal extension) toggles .is-scrolled-x and .is-scrolled-y
//     independently in LTR AND RTL (Chrome reports scrollLeft negative in RTL)
//   - Task 6.5's Overview alignment (cal-page + overview-calendar dual classes
//     on root; cal-toolbar + overview-calendar__toolbar on toolbar) makes
//     sticky CSS reach Overview without selector duplication
//   - Task 16's mobile bottom-sheet opens with cloned controls preserved
//     as source-of-truth (6 cloned children verified at runtime)
//
// To run this spec under CI: requires Node + @playwright/test installed
// (the project does not currently bundle Node tooling — install via:
//    npm install --save-dev @playwright/test
//    npx playwright install chromium
// Then: npx playwright test tests/visual/sticky-headers/baseline.spec.js
// The first run captures baselines; subsequent runs diff against them.

const { test, expect } = require('@playwright/test');

const APP_URL = process.env.SHIFTMANAGER_URL || 'http://localhost:5005';
const TEST_USER = { email: 'test.owner@shifty.test', password: 'TestOwner123!' };

const CALENDARS = [
    { slug: 'shifts',   url: '/Calendar/Shifts?ViewMode=month',   modeToken: { en: 'Shifts ▾',  he: 'משמרות ▾' } },
    { slug: 'oncall',   url: '/Calendar/OnCall?ViewMode=month',   modeToken: { en: 'Duty ▾',    he: 'תורנות ▾' } },
    { slug: 'chores',   url: '/Calendar/Chores?ViewMode=month',   modeToken: { en: 'Chores ▾',  he: 'מטלות ▾'  } },
    { slug: 'overview', url: '/Calendar/Overview?ViewMode=month', modeToken: { en: 'Shifts ▾',  he: 'משמרות ▾' } },
];

const THEMES = ['light', 'dark'];
const LANGS = [
    { code: 'en', cookie: 'c=en-US|uic=en-US' },
    { code: 'he', cookie: 'c=he-IL|uic=he-IL' },
];

// Scroll states: name + (dx, dy) where dx may be negative in RTL.
// The rtl flag flips dx sign at scroll time.
const SCROLL_STATES = [
    { name: 'rest',          dx: 0,    dy: 0   },
    { name: 'scrolled-x',    dx: 800,  dy: 0   },
    { name: 'scrolled-y',    dx: 0,    dy: 300 },
    { name: 'scrolled-both', dx: 800,  dy: 300 },
];

const VIEWPORT = { width: 1366, height: 768 };

test.use({ viewport: VIEWPORT });

async function login(page) {
    await page.goto(`${APP_URL}/Auth/Login`);
    await page.getByRole('textbox', { name: 'Email' }).fill(TEST_USER.email);
    await page.getByRole('textbox', { name: 'Password' }).fill(TEST_USER.password);
    await page.getByRole('button', { name: 'Login' }).click();
    await page.waitForURL(/\/Home/, { timeout: 10000 });
}

async function setCultureAndTheme(page, langCookie, theme) {
    await page.context().addCookies([{
        name: '.AspNetCore.Culture',
        value: langCookie,
        url: APP_URL,
    }]);
    await page.evaluate((t) => {
        document.documentElement.setAttribute('data-theme', t);
    }, theme);
}

async function captureBaselines(page, cal) {
    for (const theme of THEMES) {
        for (const lang of LANGS) {
            await setCultureAndTheme(page, lang.cookie, theme);
            await page.goto(`${APP_URL}${cal.url}`);
            await page.evaluate((t) => {
                document.documentElement.setAttribute('data-theme', t);
            }, theme);
            await page.waitForSelector('.excel-calendar', { timeout: 5000 });
            const isRtl = lang.code === 'he';
            for (const scroll of SCROLL_STATES) {
                await page.evaluate(({ dx, dy, rtl }) => {
                    const c = document.querySelector('.excel-calendar');
                    c.scrollLeft = rtl ? -dx : dx;
                    c.scrollTop  = dy;
                }, { dx: scroll.dx, dy: scroll.dy, rtl: isRtl });
                // Let IntersectionObserver settle.
                await page.waitForTimeout(200);
                await expect(page).toHaveScreenshot(
                    `${cal.slug}-${theme}-${lang.code}-${scroll.name}.png`,
                    { maxDiffPixelRatio: 0.005 }
                );
            }
        }
    }
}

test.describe('calendar sticky headers — 64 baselines', () => {
    test.beforeEach(async ({ page }) => {
        await login(page);
    });

    for (const cal of CALENDARS) {
        test(`${cal.slug} matrix`, async ({ page }) => {
            await captureBaselines(page, cal);
        });
    }
});

test.describe('mobile (414×896)', () => {
    test.use({ viewport: { width: 414, height: 896 } });

    test('Tools overflow sheet renders cloned controls', async ({ page }) => {
        await login(page);
        await page.goto(`${APP_URL}/Calendar/Shifts?ViewMode=month`);
        await page.waitForSelector('.cal-toolbar__overflow-trigger');
        // Trigger is visible at <768px
        const triggerVisible = await page.locator('.cal-toolbar__overflow-trigger').isVisible();
        expect(triggerVisible).toBe(true);

        // Open the sheet
        await page.evaluate(() => window.CalendarToolsOverflow.open());
        await page.waitForSelector('.calendar-tools-sheet.bottom-sheet--open');
        const clonedCount = await page.evaluate(() =>
            document.getElementById('calendarToolsSheetBody').children.length
        );
        expect(clonedCount).toBeGreaterThan(0);

        await expect(page).toHaveScreenshot('shifts-mobile-overflow-sheet.png');
    });
});

test.describe('regression locks', () => {
    test('aria-rowcount includes thead row', async ({ page }) => {
        await login(page);
        await page.goto(`${APP_URL}/Calendar/Shifts?ViewMode=month`);
        const aria = await page.evaluate(() =>
            document.querySelector('.excel-calendar__table').getAttribute('aria-rowcount')
        );
        const bodyRows = await page.evaluate(() =>
            document.querySelectorAll('.excel-calendar tbody tr').length
        );
        // aria-rowcount = body rows + 1 (the thead row) per ARIA 1.2 §6.6.4
        expect(parseInt(aria, 10)).toBe(bodyRows + 1);
    });

    test('Overview has cal-page + cal-toolbar dual classes (Task 6.5)', async ({ page }) => {
        await login(page);
        await page.goto(`${APP_URL}/Calendar/Overview?ViewMode=month`);
        const root = await page.locator('.cal-page.overview-calendar').count();
        const toolbar = await page.locator('.cal-toolbar.overview-calendar__toolbar').count();
        expect(root).toBe(1);
        expect(toolbar).toBe(1);
    });

    test('axis discrimination — is-scrolled-x ≠ is-scrolled-y', async ({ page }) => {
        await login(page);
        await page.goto(`${APP_URL}/Calendar/Shifts?ViewMode=month`);
        await page.evaluate(() => {
            const c = document.querySelector('.excel-calendar');
            c.scrollLeft = 600; c.scrollTop = 0;
        });
        await page.waitForTimeout(300);
        const xOnly = await page.evaluate(() => {
            const c = document.querySelector('.excel-calendar');
            return { x: c.classList.contains('is-scrolled-x'), y: c.classList.contains('is-scrolled-y') };
        });
        expect(xOnly).toEqual({ x: true, y: false });

        await page.evaluate(() => {
            const c = document.querySelector('.excel-calendar');
            c.scrollLeft = 0; c.scrollTop = 200;
        });
        await page.waitForTimeout(300);
        const yOnly = await page.evaluate(() => {
            const c = document.querySelector('.excel-calendar');
            return { x: c.classList.contains('is-scrolled-x'), y: c.classList.contains('is-scrolled-y') };
        });
        expect(yOnly).toEqual({ x: false, y: true });
    });
});
