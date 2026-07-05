using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Issue 5: the "Hebrew default" flag (FF_HEBREW_DEFAULT) flips DefaultRequestCulture to he-IL, but
/// DefaultRequestCulture is only the fallback used when EVERY culture provider returns null. The
/// AcceptLanguageHeaderRequestCultureProvider resolves en-US for any English browser first, so a
/// cookieless user never reaches the he-IL default. Fix: when the Hebrew default is on, do NOT
/// register the Accept-Language provider — query-string and cookie providers still let explicit
/// choices win, but a user with no explicit choice falls through to the he-IL default.
/// </summary>
public class RequestLocalizationSetupTests
{
    [Fact]
    public void Configure_HebrewDefaultOn_UsesHebrewDefault_AndDropsAcceptLanguageProvider()
    {
        var options = new RequestLocalizationOptions();

        RequestLocalizationSetup.Configure(options, hebrewDefaultEnabled: true);

        options.DefaultRequestCulture.Culture.Name.Should().Be("he-IL");
        options.DefaultRequestCulture.UICulture.Name.Should().Be("he-IL");
        options.RequestCultureProviders.Should()
            .NotContain(p => p is AcceptLanguageHeaderRequestCultureProvider,
                "the browser Accept-Language header must not pre-empt the Hebrew org default for cookieless users");
        // Explicit choices must still win.
        options.RequestCultureProviders.Should().Contain(p => p is QueryStringRequestCultureProvider);
        options.RequestCultureProviders.Should().Contain(p => p is CookieRequestCultureProvider);
    }

    [Fact]
    public void Configure_HebrewDefaultOff_UsesEnglishDefault_AndKeepsAcceptLanguageProvider()
    {
        var options = new RequestLocalizationOptions();

        RequestLocalizationSetup.Configure(options, hebrewDefaultEnabled: false);

        options.DefaultRequestCulture.Culture.Name.Should().Be("en-US");
        options.RequestCultureProviders.Should()
            .Contain(p => p is AcceptLanguageHeaderRequestCultureProvider,
                "with no Hebrew default, the browser header still drives culture as before");
    }

    [Fact]
    public void Configure_SupportsBothCultures_InEitherMode()
    {
        foreach (var hebrew in new[] { true, false })
        {
            var options = new RequestLocalizationOptions();
            RequestLocalizationSetup.Configure(options, hebrewDefaultEnabled: hebrew);
            options.SupportedCultures.Should().Contain(c => c.Name == "en-US");
            options.SupportedCultures.Should().Contain(c => c.Name == "he-IL");
            options.SupportedUICultures.Should().Contain(c => c.Name == "he-IL");
        }
    }
}
