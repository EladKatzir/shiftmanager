// @ts-check
const AxeBuilder = require('@axe-core/playwright').default;

/**
 * Run accessibility audit on current page
 * @param {import('@playwright/test').Page} page
 * @param {Object} [options]
 * @param {string[]} [options.exclude] - Selectors to exclude
 * @param {string[]} [options.include] - Selectors to include (defaults to full page)
 * @param {string[]} [options.disableRules] - Rules to disable
 * @returns {Promise<import('axe-core').AxeResults>}
 */
async function runAccessibilityAudit(page, options = {}) {
    let builder = new AxeBuilder({ page });

    if (options.exclude) {
        for (const selector of options.exclude) {
            builder = builder.exclude(selector);
        }
    }

    if (options.include) {
        for (const selector of options.include) {
            builder = builder.include(selector);
        }
    }

    if (options.disableRules) {
        builder = builder.disableRules(options.disableRules);
    }

    // Target WCAG AA compliance
    builder = builder.withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']);

    return builder.analyze();
}

/**
 * Format accessibility violations for readable output
 * @param {import('axe-core').Result[]} violations
 * @returns {string}
 */
function formatViolations(violations) {
    if (violations.length === 0) {
        return 'No accessibility violations found';
    }

    return violations.map(v => {
        const nodes = v.nodes.map(n => `  - ${n.html.substring(0, 100)}...`).join('\n');
        return `[${v.impact?.toUpperCase()}] ${v.id}: ${v.description}\n${nodes}`;
    }).join('\n\n');
}

/**
 * Get critical violations (serious or critical impact)
 * @param {import('axe-core').Result[]} violations
 */
function getCriticalViolations(violations) {
    return violations.filter(v => v.impact === 'critical' || v.impact === 'serious');
}

/**
 * Check color contrast ratio
 * @param {import('@playwright/test').Page} page
 * @param {string} selector - Element to check
 */
async function checkColorContrast(page, selector) {
    const element = page.locator(selector).first();

    const styles = await element.evaluate((el) => {
        const computed = getComputedStyle(el);
        return {
            color: computed.color,
            backgroundColor: computed.backgroundColor,
            fontSize: computed.fontSize,
            fontWeight: computed.fontWeight
        };
    });

    return styles;
}

module.exports = {
    runAccessibilityAudit,
    formatViolations,
    getCriticalViolations,
    checkColorContrast
};
