using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data
{
    public class TestDataSeeder
    {
        private readonly AppDbContext _context;
        private readonly ILogger<TestDataSeeder> _logger;

        public TestDataSeeder(
            AppDbContext context,
            ILogger<TestDataSeeder> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task SeedTestUsersAsync()
        {
            // Only seed in development environment
            var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
            if (!isDevelopment)
            {
                _logger.LogInformation("Skipping test data seeding - not in Development environment");
                return;
            }

            _logger.LogInformation("Starting test data seeding...");

            // Create test company if needed
            var testCompany = await EnsureTestCompanyExists();

            // Create test users with different roles
            await CreateTestUser("owner@test.com", "123456", UserRole.Owner, "Test Owner", testCompany.Id);
            await CreateTestUser("director@test.com", "123456", UserRole.Director, "Test Director", testCompany.Id);
            await CreateTestUser("manager@test.com", "123456", UserRole.Manager, "Test Manager", testCompany.Id);
            await CreateTestUser("employee@test.com", "123456", UserRole.Employee, "Test Employee", testCompany.Id);
            await CreateTestUser("assigner@test.com", "123456", UserRole.Assigner, "Test Assigner", testCompany.Id);
            await CreateTestUser("trainee@test.com", "123456", UserRole.Trainee, "Test Trainee", testCompany.Id);

            _logger.LogInformation("Test data seeding completed");
        }

        private async Task<Company> EnsureTestCompanyExists()
        {
            var testCompany = await _context.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Slug == "test-company");

            if (testCompany == null)
            {
                testCompany = new Company
                {
                    Name = "Test Company",
                    Slug = "test-company",
                    DisplayName = "Test Company"
                };
                _context.Companies.Add(testCompany);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created test company: {CompanySlug}", testCompany.Slug);
            }

            return testCompany;
        }

        private async Task CreateTestUser(string email, string password, UserRole role, string displayName, int companyId)
        {
            var existingUser = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email);

            if (existingUser != null)
            {
                _logger.LogInformation("Test user already exists: {Email}", email);
                return;
            }

            var (hash, salt) = PasswordHasher.CreateHash(password);
            var user = new AppUser
            {
                Email = email,
                DisplayName = displayName,
                CompanyId = companyId,
                Role = role,
                IsActive = true,
                PasswordHash = hash,
                PasswordSalt = salt
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created test user: {Email} with role {Role}", email, role);
        }
    }
}
