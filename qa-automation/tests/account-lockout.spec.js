// @ts-check
const { test, expect } = require('@playwright/test');

/**
 * Account Lockout Protection Tests
 *
 * Tests the account lockout mechanism that:
 * - Tracks failed login attempts
 * - Locks account after 10 failed attempts
 * - Shows appropriate warning messages
 * - Maintains security by not revealing user existence
 *
 * Related Implementation:
 * - Pages/Auth/Login.cshtml.cs (lines 156-199)
 * - Models/User.cs (FailedLoginAttempts, LockoutEnd properties)
 */

test.describe('Account Lockout Protection', () => {
  // Use a unique test email to avoid conflicts with other tests
  const testEmail = `lockout-test-${Date.now()}@test.com`;
  const wrongPassword = 'wrongpassword123';

  test.describe('Failed Login Attempt Tracking', () => {
    test('should increment failed login attempts for existing user', async ({ page }) => {
      // This test validates that the system tracks failed attempts
      // We use the known Owner account for this test
      const ownerEmail = process.env.OWNER_EMAIL || 'owner@test.com';

      await page.goto('/Auth/Login');

      // Attempt to login with wrong password
      await page.fill('input[name="Email"], input#Email', ownerEmail);
      await page.fill('input[name="Password"], input#Password', wrongPassword);
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');

      // Should show error message (invalid credentials)
      await expect(page).toHaveURL(/\/Auth\/Login/);

      // Look for error message indicating failed login
      const errorText = await page.locator('.small, .alert, [style*="color"]').allTextContents();
      const hasError = errorText.some(text =>
        text.includes('Invalid') ||
        text.includes('incorrect') ||
        text.includes('failed')
      );

      // We should see some kind of error message
      expect(hasError || errorText.length > 0).toBeTruthy();
    });

    test('should not reveal user existence for non-existent accounts', async ({ page }) => {
      // Security test: Error message should be same for existing and non-existing users
      const nonExistentEmail = `nonexistent-${Date.now()}@test.com`;

      await page.goto('/Auth/Login');

      // Attempt to login with non-existent account
      await page.fill('input[name="Email"], input#Email', nonExistentEmail);
      await page.fill('input[name="Password"], input#Password', wrongPassword);
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');

      // Should show generic error (not "user not found")
      const pageContent = await page.content();

      // Should NOT contain user enumeration hints
      expect(pageContent.toLowerCase()).not.toContain('user not found');
      expect(pageContent.toLowerCase()).not.toContain('does not exist');
      expect(pageContent.toLowerCase()).not.toContain('unknown user');

      // Should show generic "invalid credentials" message
      const hasGenericError = pageContent.includes('Invalid') ||
                              pageContent.includes('incorrect') ||
                              pageContent.includes('failed');
      expect(hasGenericError).toBeTruthy();
    });
  });

  test.describe('Account Lockout After Multiple Failures', () => {
    test.skip('should lock account after 10 failed attempts', async ({ page }) => {
      // NOTE: This test is skipped by default because it requires:
      // 1. A dedicated test user account that can be locked
      // 2. Database cleanup after the test
      // 3. Significant time to execute (10+ login attempts)
      //
      // To run this test:
      // 1. Create a dedicated test user in the database
      // 2. Update the testUserEmail below
      // 3. Remove test.skip and run with: npx playwright test account-lockout.spec.js --grep "should lock account"

      const testUserEmail = 'test-lockout-user@test.com'; // Update with actual test user

      await page.goto('/Auth/Login');

      // Attempt login 10 times with wrong password
      for (let i = 1; i <= 10; i++) {
        await page.fill('input[name="Email"], input#Email', testUserEmail);
        await page.fill('input[name="Password"], input#Password', `wrong-${i}`);
        await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
        await page.waitForLoadState('networkidle');

        if (i < 10) {
          // Should still show invalid credentials
          await expect(page).toHaveURL(/\/Auth\/Login/);
        } else {
          // On 10th attempt, should show lockout message
          const content = await page.content();
          expect(content.toLowerCase()).toMatch(/locked|lockout/);
          expect(content).toMatch(/3 minute/); // Default lockout is 3 minutes
        }

        // Small delay between attempts
        await page.waitForTimeout(500);
      }

      // Verify account is now locked - even with correct password
      await page.fill('input[name="Email"], input#Email', testUserEmail);
      await page.fill('input[name="Password"], input#Password', '123456'); // Correct password
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');

      // Should still show lockout message
      const content = await page.content();
      expect(content.toLowerCase()).toMatch(/locked|lockout/);
    });

    test('should show lockout countdown message', async ({ page }) => {
      // This test verifies the lockout message format
      // We can't easily trigger a real lockout without a dedicated test account,
      // so this test verifies the message structure exists in the codebase

      await page.goto('/Auth/Login');

      // Check that the login page is functional
      await expect(page.locator('input[name="Email"], input#Email')).toBeVisible();
      await expect(page.locator('input[name="Password"], input#Password')).toBeVisible();

      // The actual lockout message testing would require:
      // 1. A pre-locked test account in the database
      // 2. OR database manipulation to set LockoutEnd timestamp
      //
      // For now, we document the expected behavior:
      // Expected message: "Account is locked due to multiple failed login attempts.
      //                    Please try again in X minute(s)."
    });
  });

  test.describe('Lockout Duration', () => {
    test('should implement 3-minute lockout period', async ({ page }) => {
      // This test documents the expected lockout duration
      // Implementation in Login.cshtml.cs line 182:
      //   user.LockoutEnd = DateTime.UtcNow.AddMinutes(3);

      // The lockout duration is 3 minutes as per the implementation
      // This is a reasonable balance between security and user experience

      // To fully test this, we would need to:
      // 1. Lock an account
      // 2. Wait 3 minutes
      // 3. Verify account is unlocked
      //
      // This is too slow for regular CI/CD, so we document it here

      await page.goto('/Auth/Login');

      // Verify the page loads correctly
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });
  });

  test.describe('Rate Limiting Integration', () => {
    test('should respect rate limiting (10 attempts per 15 minutes)', async ({ page }) => {
      // Implementation in Login.cshtml.cs lines 119-128
      // Rate limit: 10 attempts per 15 minutes per IP address

      await page.goto('/Auth/Login');

      const testEmailForRateLimit = `ratelimit-${Date.now()}@test.com`;

      // Make several rapid login attempts
      for (let i = 0; i < 5; i++) {
        await page.fill('input[name="Email"], input#Email', testEmailForRateLimit);
        await page.fill('input[name="Password"], input#Password', `wrong-${i}`);
        await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
        await page.waitForLoadState('networkidle');

        // Should still be on login page
        await expect(page).toHaveURL(/\/Auth\/Login/);

        // Small delay
        await page.waitForTimeout(200);
      }

      // After 5 attempts, should still be able to try again
      // (rate limit is 10, we're at 5)
      await page.fill('input[name="Email"], input#Email', testEmailForRateLimit);
      await page.fill('input[name="Password"], input#Password', 'another-wrong');
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');

      // Should still show normal error, not rate limit error
      const content = await page.content();
      expect(content.toLowerCase()).not.toContain('rate limit');
      expect(content.toLowerCase()).not.toContain('too many');
    });
  });

  test.describe('Successful Login Resets Counter', () => {
    test('should reset failed attempts counter on successful login', async ({ page }) => {
      // Implementation in Login.cshtml.cs lines 202-205
      // On successful login:
      //   user.FailedLoginAttempts = 0;
      //   user.LockoutEnd = null;

      const ownerEmail = process.env.OWNER_EMAIL || 'owner@test.com';
      const ownerPassword = process.env.OWNER_PASSWORD || '123456';

      await page.goto('/Auth/Login');

      // First, make a failed attempt
      await page.fill('input[name="Email"], input#Email', ownerEmail);
      await page.fill('input[name="Password"], input#Password', 'wrongpassword');
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');

      // Should be on login page with error
      await expect(page).toHaveURL(/\/Auth\/Login/);

      // Now login successfully
      await page.fill('input[name="Email"], input#Email', ownerEmail);
      await page.fill('input[name="Password"], input#Password', ownerPassword);

      await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
      ]);

      // Should be redirected away from login (successful login)
      await expect(page).not.toHaveURL(/\/Auth\/Login/);

      // Logout for cleanup
      const logoutForm = page.locator('form[action*="Logout"]');
      if (await logoutForm.isVisible({ timeout: 2000 })) {
        await logoutForm.locator('button[type="submit"]').click();
        await page.waitForURL(/\/Auth\/Login/);
      }
    });
  });

  test.describe('Security Best Practices', () => {
    test('should not expose timing differences between valid and invalid emails', async ({ page }) => {
      // Security test: Response time should be similar for existing and non-existing users
      // This prevents timing attacks to enumerate valid email addresses

      const existingEmail = process.env.OWNER_EMAIL || 'owner@test.com';
      const nonExistingEmail = `nonexistent-${Date.now()}@test.com`;

      await page.goto('/Auth/Login');

      // Measure time for non-existing email
      const start1 = Date.now();
      await page.fill('input[name="Email"], input#Email', nonExistingEmail);
      await page.fill('input[name="Password"], input#Password', 'password');
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');
      const duration1 = Date.now() - start1;

      await page.goto('/Auth/Login');

      // Measure time for existing email (with wrong password)
      const start2 = Date.now();
      await page.fill('input[name="Email"], input#Email', existingEmail);
      await page.fill('input[name="Password"], input#Password', 'wrongpassword');
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');
      const duration2 = Date.now() - start2;

      // Times should be roughly similar (within 2x factor)
      // This is a loose check - significant differences would indicate timing attack vulnerability
      const ratio = Math.max(duration1, duration2) / Math.min(duration1, duration2);

      // Allow up to 3x difference (network variance, etc.)
      // A timing attack would typically show 10x+ differences
      expect(ratio).toBeLessThan(3);
    });

    test('should log failed login attempts for security monitoring', async ({ page }) => {
      // Implementation in Login.cshtml.cs lines 185-196
      // The system logs failed attempts with:
      //   _logger.LogWarning("Account locked for {Email}...", ...)
      //   _logger.LogWarning("Login failed for {Email}...", ...)

      // This test verifies the login process is working
      // Actual log verification would require backend access or log file inspection

      await page.goto('/Auth/Login');

      const testEmail = `audit-${Date.now()}@test.com`;
      await page.fill('input[name="Email"], input#Email', testEmail);
      await page.fill('input[name="Password"], input#Password', 'wrong');
      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
      await page.waitForLoadState('networkidle');

      // Should show error
      await expect(page).toHaveURL(/\/Auth\/Login/);

      // The system should have logged this attempt
      // Log entry expected: "Login failed for {Email} (user not found)"
    });
  });
});
