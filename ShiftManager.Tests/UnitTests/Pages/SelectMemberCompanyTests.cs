using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Pages.Api;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Unit tests for <see cref="SelectMemberCompanyModel"/>.
/// Verifies POST-only guard, SelectCompanyAsync is called, and returnUrl redirect logic.
/// Uses a mocked <see cref="IActiveCompanySelectorService"/> — no DB required.
/// </summary>
public sealed class SelectMemberCompanyTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the page model wired with the provided mock selector and an authenticated
    /// HttpContext so Url.IsLocalUrl works correctly. The audit mock is optional; pass one
    /// to assert audit-log behavior.
    /// </summary>
    private static SelectMemberCompanyModel BuildModel(
        Mock<IActiveCompanySelectorService> selectorMock,
        Mock<IAuditLogService>? auditMock = null)
    {
        var model = new SelectMemberCompanyModel(
            selectorMock.Object,
            (auditMock ?? new Mock<IAuditLogService>()).Object,
            NullLogger<SelectMemberCompanyModel>.Instance);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "42") }, "test"))
        };

        var pageContext = new PageContext
        {
            HttpContext = httpContext
        };

        model.PageContext = pageContext;

        // Wire an ActionContext + UrlHelper so Url.IsLocalUrl works
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        model.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(actionContext);

        return model;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OnGet_ReturnsRedirectToIndexPage()
    {
        var selector = new Mock<IActiveCompanySelectorService>();
        var model = BuildModel(selector);

        var result = model.OnGet();

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/Index");
    }

    [Fact]
    public async Task OnPostAsync_CallsSelectCompanyAsync_WithProvidedCompanyId()
    {
        const int companyId = 99;
        var selector = new Mock<IActiveCompanySelectorService>();
        selector.Setup(s => s.SelectCompanyAsync(companyId)).ReturnsAsync(true);

        var model = BuildModel(selector);

        await model.OnPostAsync(companyId);

        selector.Verify(s => s.SelectCompanyAsync(companyId), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_ValidLocalReturnUrl_RedirectsToReturnUrl()
    {
        const int companyId = 7;
        const string returnUrl = "/Calendar/Shifts";
        var selector = new Mock<IActiveCompanySelectorService>();
        selector.Setup(s => s.SelectCompanyAsync(It.IsAny<int>())).ReturnsAsync(true);

        var model = BuildModel(selector);

        var result = await model.OnPostAsync(companyId, returnUrl);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be(returnUrl);
    }

    [Fact]
    public async Task OnPostAsync_NullReturnUrl_RedirectsToIndexPage()
    {
        const int companyId = 7;
        var selector = new Mock<IActiveCompanySelectorService>();
        selector.Setup(s => s.SelectCompanyAsync(It.IsAny<int>())).ReturnsAsync(true);

        var model = BuildModel(selector);

        var result = await model.OnPostAsync(companyId, returnUrl: null);

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/Index");
    }

    [Fact]
    public async Task OnPostAsync_EmptyReturnUrl_RedirectsToIndexPage()
    {
        const int companyId = 7;
        var selector = new Mock<IActiveCompanySelectorService>();
        selector.Setup(s => s.SelectCompanyAsync(It.IsAny<int>())).ReturnsAsync(true);

        var model = BuildModel(selector);

        var result = await model.OnPostAsync(companyId, returnUrl: "");

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/Index");
    }

    [Fact]
    public async Task OnPostAsync_SelectCompanyReturnsFalse_StillRedirectsToReturnUrl()
    {
        // When membership gate denies access, no cookie is set (inside the service),
        // but the page still redirects — the selector already logged/handled the denial.
        const int companyId = 999;
        const string returnUrl = "/Calendar/Shifts";
        var selector = new Mock<IActiveCompanySelectorService>();
        selector.Setup(s => s.SelectCompanyAsync(companyId)).ReturnsAsync(false);

        var model = BuildModel(selector);

        var result = await model.OnPostAsync(companyId, returnUrl);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be(returnUrl);
        // SelectCompanyAsync was still called (the handler delegates the gate to the service)
        selector.Verify(s => s.SelectCompanyAsync(companyId), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_SuccessfulSwitch_WritesAuditLog()
    {
        const int companyId = 12;
        var selector = new Mock<IActiveCompanySelectorService>();
        selector.Setup(s => s.SelectCompanyAsync(companyId)).ReturnsAsync(true);
        var audit = new Mock<IAuditLogService>();

        var model = BuildModel(selector, audit);

        await model.OnPostAsync(companyId, "/Calendar/Shifts");

        // userId 42 comes from the NameIdentifier claim set in BuildModel.
        audit.Verify(a => a.LogUserActionAsync(
            42,
            "MemberSelectedCompany",
            "CompanySelection",
            companyId,
            It.Is<string>(d => d.Contains(companyId.ToString())),
            null), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_DeniedSwitch_DoesNotWriteAuditLog()
    {
        const int companyId = 999;
        var selector = new Mock<IActiveCompanySelectorService>();
        selector.Setup(s => s.SelectCompanyAsync(companyId)).ReturnsAsync(false);
        var audit = new Mock<IAuditLogService>();

        var model = BuildModel(selector, audit);

        await model.OnPostAsync(companyId, "/Calendar/Shifts");

        // No audit entry on a denied (non-member) switch — only successful switches are logged.
        audit.Verify(a => a.LogUserActionAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }
}
