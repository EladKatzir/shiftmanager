using FluentAssertions;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.Security;

/// <summary>
/// Tests for the stateless ValidationService.
/// Covers email, phone, URL, safe string validation, and search normalization.
/// </summary>
public class InputValidationTests : MasterTestBase
{
    public InputValidationTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // EMAIL VALIDATION
    // ================================================================

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("test.name+tag@domain.co.uk")]
    [InlineData("a@b.c")]
    [InlineData("user123@test-domain.org")]
    public void IsValidEmail_ValidEmails_ReturnsTrue(string email)
    {
        var service = CreateValidationService();
        service.IsValidEmail(email).Should().BeTrue($"'{email}' should be a valid email");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@missing.com")]
    [InlineData("user@")]
    public void IsValidEmail_InvalidEmails_ReturnsFalse(string? email)
    {
        var service = CreateValidationService();
        service.IsValidEmail(email).Should().BeFalse($"'{email}' should be an invalid email");
    }

    [Fact]
    public void IsValidEmail_TooLong_ReturnsFalse()
    {
        var service = CreateValidationService();
        // 256 chars total exceeds RFC 5321 max of 254
        var longEmail = new string('a', 246) + "@test.com";
        service.IsValidEmail(longEmail).Should().BeFalse(
            "Email exceeding 254 characters should be rejected");
    }

    // ================================================================
    // PHONE VALIDATION
    // ================================================================

    [Theory]
    [InlineData("1234567890")]
    [InlineData("+1-234-567-8900")]
    [InlineData("(123) 456-7890")]
    [InlineData("+44 20 1234 5678")]
    public void IsValidPhone_ValidPhones_ReturnsTrue(string phone)
    {
        var service = CreateValidationService();
        service.IsValidPhone(phone).Should().BeTrue($"'{phone}' should be a valid phone");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData("")]
    public void IsValidPhone_InvalidPhones_ReturnsFalse(string? phone)
    {
        var service = CreateValidationService();
        service.IsValidPhone(phone).Should().BeFalse($"'{phone}' should be an invalid phone");
    }

    [Fact]
    public void IsValidPhone_TooLong_ReturnsFalse()
    {
        var service = CreateValidationService();
        // 21 characters exceeds the 20-char max
        var longPhone = "1" + new string('2', 20);
        service.IsValidPhone(longPhone).Should().BeFalse(
            "Phone exceeding 20 characters should be rejected");
    }

    [Fact]
    public void IsValidPhone_TooShort_ReturnsFalse()
    {
        var service = CreateValidationService();
        service.IsValidPhone("123456").Should().BeFalse(
            "Phone shorter than 7 characters should be rejected");
    }

    // ================================================================
    // SAFE STRING VALIDATION
    // ================================================================

    [Theory]
    [InlineData("Hello World")]
    [InlineData("")]
    [InlineData("Test 123, with punctuation!")]
    [InlineData("user@email.com")]
    [InlineData("It's a test")]
    public void IsSafeString_SafeInputs_ReturnsTrue(string input)
    {
        var service = CreateValidationService();
        service.IsSafeString(input).Should().BeTrue($"'{input}' should be a safe string");
    }

    [Fact]
    public void IsSafeString_NullInput_ReturnsTrue()
    {
        var service = CreateValidationService();
        service.IsSafeString(null).Should().BeTrue("Null input should be considered safe");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("{\"key\": \"value\"}")]
    [InlineData("command `backtick`")]
    public void IsSafeString_UnsafeInputs_ReturnsFalse(string input)
    {
        var service = CreateValidationService();
        service.IsSafeString(input).Should().BeFalse($"'{input}' should be an unsafe string");
    }

    // ================================================================
    // URL VALIDATION
    // ================================================================

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://test.org/path?q=1")]
    [InlineData("https://www.example.co.uk/page")]
    public void IsValidUrl_ValidUrls_ReturnsTrue(string url)
    {
        var service = CreateValidationService();
        service.IsValidUrl(url).Should().BeTrue($"'{url}' should be a valid URL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ftp://bad.com")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void IsValidUrl_InvalidUrls_ReturnsFalse(string? url)
    {
        var service = CreateValidationService();
        service.IsValidUrl(url).Should().BeFalse($"'{url}' should be an invalid URL");
    }

    [Fact]
    public void IsValidUrl_TooLong_ReturnsFalse()
    {
        var service = CreateValidationService();
        var longUrl = "https://example.com/" + new string('a', 2040);
        service.IsValidUrl(longUrl).Should().BeFalse(
            "URL exceeding 2048 characters should be rejected");
    }

    // ================================================================
    // SEARCH NORMALIZATION
    // ================================================================

    [Fact]
    public void NormalizeForSearch_TrimsWhitespace()
    {
        var service = CreateValidationService();
        service.NormalizeForSearch("  hello  ").Should().Be("hello");
    }

    [Fact]
    public void NormalizeForSearch_NullReturnsEmpty()
    {
        var service = CreateValidationService();
        service.NormalizeForSearch(null).Should().BeEmpty();
    }

    [Fact]
    public void NormalizeForSearch_EmptyReturnsEmpty()
    {
        var service = CreateValidationService();
        service.NormalizeForSearch("").Should().BeEmpty();
    }
}
