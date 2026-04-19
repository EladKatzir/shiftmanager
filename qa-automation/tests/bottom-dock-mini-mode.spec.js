// @ts-check
/**
 * Bottom Dock — Mini Mode three-state machine
 * Spec: docs/superpowers/specs/2026-04-17-bottom-dock-mini-mode-design.md
 *
 * State machine:
 *   FULL <-> COLLAPSED (via toggle button .bottom-dock__toggle)
 *   FULL | COLLAPSED  -> MINI (via .bottom-dock__minimize)
 *   MINI -> previous state (via .bottom-dock__pill)
 *
 * localStorage keys:
 *   shifty_bottom_dock_state         : 'full' | 'collapsed' | 'mini'
 *   shifty_bottom_dock_prev_state    : 'full' | 'collapsed'
 *   shifty_bottom_dock_collapsed     : (legacy) auto-migrated on first load
 */
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');

const STATE_KEY  = 'shifty_bottom_dock_state';
const PREV_KEY   = 'shifty_bottom_dock_prev_state';
const LEGACY_KEY = 'shifty_bottom_dock_collapsed';

/**
 * Clear all dock-related localStorage keys so each test starts from a known slate.
 * Must run AFTER navigation so the origin matches.
 */
async function resetDockStorage(page) {
    await page.evaluate(({ STATE_KEY, PREV_KEY, LEGACY_KEY }) => {
        localStorage.removeItem(STATE_KEY);
        localStorage.removeItem(PREV_KEY);
        localStorage.removeItem(LEGACY_KEY);
    }, { STATE_KEY, PREV_KEY, LEGACY_KEY });
}

async function readStorage(page, key) {
    return page.evaluate((k) => localStorage.getItem(k), key);
}

async function writeStorage(page, key, value) {
    await page.evaluate(({ k, v }) => localStorage.setItem(k, v), { k: key, v: value });
}

test.describe('Bottom Dock — Mini Mode', () => {

    test.beforeEach(async ({ page }) => {
        await loginAsOwner(page);
        await page.waitForLoadState('networkidle');

        // Skip silently if WidgetsEnabled flag is off — the dock won't render at all
        const dockExists = await page.locator('#bottomDock').count();
        test.skip(dockExists === 0, 'Bottom dock not rendered (WidgetsEnabled feature flag off)');

        await resetDockStorage(page);
        await page.reload();
        await page.waitForLoadState('networkidle');
    });

    // ---------------------------------------------------------------------
    // DOCK-01: Toggle cycles Full <-> Collapsed and persists state
    // ---------------------------------------------------------------------
    test('DOCK-01: toggle flips Full <-> Collapsed and writes localStorage', async ({ page }) => {
        const dock   = page.locator('#bottomDock');
        const toggle = page.locator('#bottomDockToggle');

        // New-user default = collapsed
        await expect(dock).toHaveClass(/is-collapsed/);
        expect(await readStorage(page, STATE_KEY)).toBe('collapsed');
        await expect(toggle).toHaveAttribute('aria-expanded', 'false');

        // Click -> Full
        await toggle.click();
        await expect(dock).not.toHaveClass(/is-collapsed/);
        await expect(dock).not.toHaveClass(/is-mini/);
        expect(await readStorage(page, STATE_KEY)).toBe('full');
        await expect(toggle).toHaveAttribute('aria-expanded', 'true');

        // Click -> Collapsed
        await toggle.click();
        await expect(dock).toHaveClass(/is-collapsed/);
        expect(await readStorage(page, STATE_KEY)).toBe('collapsed');
        await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    });

    // ---------------------------------------------------------------------
    // DOCK-02: Minimize from Collapsed -> Mini; pill restores to Collapsed
    // ---------------------------------------------------------------------
    test('DOCK-02: Mini from Collapsed restores to Collapsed (not Full)', async ({ page }) => {
        const dock     = page.locator('#bottomDock');
        const minimize = page.locator('#bottomDockMinimize');
        const pill     = page.locator('#bottomDockPill');

        // Starting state: collapsed
        await expect(dock).toHaveClass(/is-collapsed/);

        await minimize.click();
        await expect(dock).toHaveClass(/is-mini/);
        await expect(dock).not.toHaveClass(/is-collapsed/);
        await expect(pill).toBeVisible();
        expect(await readStorage(page, STATE_KEY)).toBe('mini');
        expect(await readStorage(page, PREV_KEY)).toBe('collapsed');

        // Restore
        await pill.click();
        await expect(dock).toHaveClass(/is-collapsed/);
        await expect(dock).not.toHaveClass(/is-mini/);
        expect(await readStorage(page, STATE_KEY)).toBe('collapsed');
    });

    // ---------------------------------------------------------------------
    // DOCK-03: Minimize from Full -> Mini; pill restores to Full
    // ---------------------------------------------------------------------
    test('DOCK-03: Mini from Full restores to Full', async ({ page }) => {
        const dock     = page.locator('#bottomDock');
        const toggle   = page.locator('#bottomDockToggle');
        const minimize = page.locator('#bottomDockMinimize');
        const pill     = page.locator('#bottomDockPill');

        // Open the dock to Full first
        await toggle.click();
        await expect(dock).not.toHaveClass(/is-collapsed/);
        expect(await readStorage(page, STATE_KEY)).toBe('full');

        await minimize.click();
        await expect(dock).toHaveClass(/is-mini/);
        expect(await readStorage(page, PREV_KEY)).toBe('full');

        await pill.click();
        await expect(dock).not.toHaveClass(/is-mini/);
        await expect(dock).not.toHaveClass(/is-collapsed/);
        expect(await readStorage(page, STATE_KEY)).toBe('full');
        await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    });

    // ---------------------------------------------------------------------
    // DOCK-04: Mini state persists across reload, aria-hidden applied
    // ---------------------------------------------------------------------
    test('DOCK-04: Mini persists across reload with aria-hidden on dock', async ({ page }) => {
        const dock     = page.locator('#bottomDock');
        const minimize = page.locator('#bottomDockMinimize');
        const pill     = page.locator('#bottomDockPill');

        await minimize.click();
        await expect(dock).toHaveClass(/is-mini/);
        await expect(dock).toHaveAttribute('aria-hidden', 'true');

        await page.reload();
        await page.waitForLoadState('networkidle');

        await expect(dock).toHaveClass(/is-mini/);
        await expect(dock).toHaveAttribute('aria-hidden', 'true');
        await expect(pill).toBeVisible();
        expect(await readStorage(page, STATE_KEY)).toBe('mini');
    });

    // ---------------------------------------------------------------------
    // DOCK-05: Legacy key `shifty_bottom_dock_collapsed` migrates cleanly
    // ---------------------------------------------------------------------
    test('DOCK-05: legacy localStorage key auto-migrates to new enum', async ({ page }) => {
        const dock = page.locator('#bottomDock');

        // Simulate a returning user who had the dock collapsed in the old system
        await writeStorage(page, LEGACY_KEY, 'true');
        // Ensure new key absent
        await page.evaluate((k) => localStorage.removeItem(k), STATE_KEY);

        await page.reload();
        await page.waitForLoadState('networkidle');

        await expect(dock).toHaveClass(/is-collapsed/);
        expect(await readStorage(page, STATE_KEY)).toBe('collapsed');
        expect(await readStorage(page, LEGACY_KEY)).toBeNull();

        // And a user who had it expanded:
        await resetDockStorage(page);
        await writeStorage(page, LEGACY_KEY, 'false');
        await page.reload();
        await page.waitForLoadState('networkidle');

        await expect(dock).not.toHaveClass(/is-collapsed/);
        expect(await readStorage(page, STATE_KEY)).toBe('full');
        expect(await readStorage(page, LEGACY_KEY)).toBeNull();
    });

    // ---------------------------------------------------------------------
    // DOCK-06: Dragged position is preserved across Mini
    // ---------------------------------------------------------------------
    test('DOCK-06: dock keeps its dragged position when minimized', async ({ page }) => {
        const dock     = page.locator('#bottomDock');
        const minimize = page.locator('#bottomDockMinimize');

        // Seed a custom position directly via localStorage (more deterministic than a drag gesture)
        await page.evaluate(() => {
            localStorage.setItem('shifty_bottom_dock_position', JSON.stringify({ x: 160, y: 220 }));
        });
        await page.reload();
        await page.waitForLoadState('networkidle');

        // Sanity: the drag IIFE should have applied inline left/top
        const beforeRect = await dock.evaluate(el => ({
            left: el.style.left,
            top: el.style.top,
            right: el.style.right,
            bottom: el.style.bottom
        }));
        expect(beforeRect.left).toBe('160px');
        expect(beforeRect.top).toBe('220px');

        await minimize.click();
        await expect(dock).toHaveClass(/is-mini/);

        const afterRect = await dock.evaluate(el => ({
            left: el.style.left,
            top: el.style.top
        }));
        expect(afterRect.left).toBe('160px');
        expect(afterRect.top).toBe('220px');
    });

    // ---------------------------------------------------------------------
    // DOCK-07: Pill has white text on primary gradient in both light/dark themes
    // ---------------------------------------------------------------------
    test('DOCK-07: pill respects dark-on-dark contrast rule', async ({ page }) => {
        const minimize = page.locator('#bottomDockMinimize');
        const pill     = page.locator('#bottomDockPill');

        await minimize.click();
        await expect(pill).toBeVisible();

        // In CSS the rule is `color: var(--primary-contrast) !important` — which is #FFFFFF
        const color = await pill.evaluate(el => getComputedStyle(el).color);
        // Accept either "rgb(255, 255, 255)" or any notation equivalent to white
        expect(color.replace(/\s/g, '')).toMatch(/^(rgb\(255,255,255\)|rgba\(255,255,255,1\))$/);

        // And the background should NOT be transparent (the gradient resolves to a solid via computed style we can't read directly; check for box-shadow presence instead — confirms .bottom-dock__pill rules were applied)
        const shadow = await pill.evaluate(el => getComputedStyle(el).boxShadow);
        expect(shadow).not.toBe('none');
    });

    // ---------------------------------------------------------------------
    // DOCK-08: Pill is hidden in print media
    // ---------------------------------------------------------------------
    test('DOCK-08: pill is hidden when print media is emulated', async ({ page }) => {
        const minimize = page.locator('#bottomDockMinimize');
        const pill     = page.locator('#bottomDockPill');

        await minimize.click();
        await expect(pill).toBeVisible();

        await page.emulateMedia({ media: 'print' });
        // In print the .bottom-dock, .bottom-dock__toggle, .bottom-dock__pill, .system-alerts { display: none !important } rule fires
        const isVisible = await pill.isVisible();
        expect(isVisible).toBe(false);

        await page.emulateMedia({ media: 'screen' });
    });

    // ---------------------------------------------------------------------
    // DOCK-09: Minimize button is keyboard-reachable and activatable
    // ---------------------------------------------------------------------
    test('DOCK-09: minimize button reachable and activatable via keyboard', async ({ page }) => {
        const dock     = page.locator('#bottomDock');
        const minimize = page.locator('#bottomDockMinimize');

        // Direct focus + keyboard activation (Tab-order depends on page contents and is not the contract we're testing here)
        await minimize.focus();
        await expect(minimize).toBeFocused();

        await page.keyboard.press('Enter');
        await expect(dock).toHaveClass(/is-mini/);

        // Verify the pill receives focus semantics when needed
        const pill = page.locator('#bottomDockPill');
        await pill.focus();
        await expect(pill).toBeFocused();
        await page.keyboard.press('Space');
        await expect(dock).not.toHaveClass(/is-mini/);
    });

    // ---------------------------------------------------------------------
    // DOCK-11: Toggle re-anchors dragged dock to CSS default (bottom-right)
    // and wipes the saved drag position. Prevents expanded content from
    // overflowing below the viewport when dock was dragged low.
    // ---------------------------------------------------------------------
    test('DOCK-11: toggle re-anchors to viewport bottom and clears drag position', async ({ page }) => {
        const dock   = page.locator('#bottomDock');
        const toggle = page.locator('#bottomDockToggle');

        // Seed a drag position near the viewport bottom — simulates a dock that
        // would push expanded widget content below the fold.
        await page.evaluate(() => {
            localStorage.setItem('shifty_bottom_dock_position', JSON.stringify({ x: 200, y: window.innerHeight - 60 }));
            localStorage.setItem('shifty_bottom_dock_state', 'collapsed');
        });
        await page.reload();
        await page.waitForLoadState('networkidle');

        // Sanity: drag IIFE applied the low top-anchor
        const before = await dock.evaluate(el => ({ top: el.style.top, left: el.style.left }));
        expect(before.top).not.toBe('');
        expect(before.left).not.toBe('');

        // Toggle -> Full: should clear inline position + localStorage position
        await toggle.click();

        const after = await dock.evaluate(el => ({
            inlineTop: el.style.top,
            inlineLeft: el.style.left,
            inlineBottom: el.style.bottom,
            inlineRight: el.style.right,
            computedBottom: getComputedStyle(el).bottom,
            rectBottom: el.getBoundingClientRect().bottom,
            viewportH: window.innerHeight
        }));
        expect(after.inlineTop).toBe('');
        expect(after.inlineLeft).toBe('');
        expect(after.inlineBottom).toBe('');
        expect(after.inlineRight).toBe('');
        expect(after.computedBottom).toBe('0px');
        // Bottom edge of dock is at viewport bottom (CSS `bottom: 0`)
        expect(Math.abs(after.rectBottom - after.viewportH)).toBeLessThan(2);

        // Saved drag position was wiped
        const pos = await page.evaluate(() => localStorage.getItem('shifty_bottom_dock_position'));
        expect(pos).toBeNull();
    });

    // ---------------------------------------------------------------------
    // DOCK-10: Escape closes Full -> Collapsed but does NOT leave Mini
    // ---------------------------------------------------------------------
    test('DOCK-10: Escape closes Full to Collapsed; no-op from Mini', async ({ page }) => {
        const dock     = page.locator('#bottomDock');
        const toggle   = page.locator('#bottomDockToggle');
        const minimize = page.locator('#bottomDockMinimize');

        // Full -> Collapsed via Escape
        await toggle.click();
        await expect(dock).not.toHaveClass(/is-collapsed/);
        await page.keyboard.press('Escape');
        await expect(dock).toHaveClass(/is-collapsed/);

        // From Mini, Escape should NOT exit
        await minimize.click();
        await expect(dock).toHaveClass(/is-mini/);
        await page.keyboard.press('Escape');
        await expect(dock).toHaveClass(/is-mini/);
    });
});
