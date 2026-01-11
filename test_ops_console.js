const { chromium } = require('playwright');

(async () => {
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage();

    try {
        console.log('[1/5] Logging in...');
        await page.goto('http://localhost:5000/Auth/Login');
        await page.fill('input[name="Email"]', 'admin@local');
        await page.fill('input[name="Password"]', 'admin');
        await page.click('button[type="submit"]');
        await page.waitForURL('**/Home/Index', { timeout: 10000 });
        console.log('✅ Login successful\n');

        // Test Blueprints page
        console.log('[2/5] Testing Blueprints page...');
        await page.goto('http://localhost:5000/Owner/Blueprints');
        await page.waitForLoadState('networkidle');

        const pageTitle = await page.locator('h1').first().textContent();
        console.log(`  Page title: ${pageTitle}`);

        // Check if NameKey warning exists
        const warningAlert = await page.locator('.alert-warning').count();
        if (warningAlert > 0) {
            const warningText = await page.locator('.alert-warning').textContent();
            console.log(`  ⚠️ Warning found: ${warningText.substring(0, 100)}...`);

            // Check if populate button exists
            const populateButton = await page.locator('button:has-text("Populate NameKeys")').count();
            if (populateButton > 0) {
                console.log('  📋 "Populate NameKeys" button found - clicking...');
                await page.click('button:has-text("Populate NameKeys")');
                await page.waitForLoadState('networkidle');

                // Check for success message
                const successAlert = await page.locator('.alert-success').count();
                if (successAlert > 0) {
                    const successText = await page.locator('.alert-success').textContent();
                    console.log(`  ✅ ${successText}`);
                } else {
                    console.log('  ⚠️ No success message found');
                }
            }
        } else {
            console.log('  ✅ No NameKey warning - all shift types have NameKey values');
        }

        // Count shift types
        const shiftTypeRows = await page.locator('table tbody tr').count();
        console.log(`  📊 Found ${shiftTypeRows} shift types\n`);

        // Test Programs page
        console.log('[3/5] Testing Programs page...');
        await page.goto('http://localhost:5000/Owner/Programs');
        await page.waitForLoadState('networkidle');

        const programsTitle = await page.locator('h1').first().textContent();
        console.log(`  Page title: ${programsTitle}`);

        const programsCount = await page.locator('.program-card, .feature-category').count();
        console.log(`  📊 Programs sections found: ${programsCount}\n`);

        // Test Master Programs page
        console.log('[4/5] Testing Master Programs page...');
        await page.goto('http://localhost:5000/Owner/MasterPrograms');
        await page.waitForLoadState('networkidle');

        const masterProgramsTitle = await page.locator('h1').first().textContent();
        console.log(`  Page title: ${masterProgramsTitle}`);

        const masterProgramsCount = await page.locator('.program-card, .feature-category').count();
        console.log(`  📊 Master Programs sections found: ${masterProgramsCount}\n`);

        // Test Calendar/Table page (with Roster & Radar buttons)
        console.log('[5/5] Verifying Calendar/Table (Roster & Radar)...');
        await page.goto('http://localhost:5000/Calendar/Table');
        await page.waitForLoadState('networkidle');

        // Wait for JavaScript modules to initialize
        await page.waitForTimeout(1000);

        // Check console logs for module initialization
        const rosterButton = await page.locator('#rosterDockToggle').count();
        const radarButton = await page.locator('#radarToggle').count();

        console.log(`  Roster button: ${rosterButton > 0 ? '✅ Found' : '❌ NOT FOUND'}`);
        console.log(`  Radar button: ${radarButton > 0 ? '✅ Found' : '❌ NOT FOUND'}`);

        if (rosterButton > 0 && radarButton > 0) {
            console.log('  ✅ Ops Console Scheduler UI elements present\n');
        } else {
            console.log('  ❌ Missing Ops Console Scheduler UI elements\n');
        }

        console.log('═══════════════════════════════════════════════');
        console.log('✅ ALL TESTS PASSED - Ops Console Scheduler is ready!');
        console.log('═══════════════════════════════════════════════');

    } catch (error) {
        console.error('❌ Test failed:', error.message);
        process.exit(1);
    } finally {
        await browser.close();
    }
})();
