using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// The memory thresholds must be configurable because the check measures WHATEVER PROCESS hosts
/// it — <c>Process.GetCurrentProcess().WorkingSet64</c>. In production that is the app, and 800MB
/// is a sensible ceiling for the air-gapped IIS deployment. Under WebApplicationFactory the host
/// is the xUnit test runner carrying every previously-executed test's retained memory, so a
/// hardcoded absolute ceiling judges the wrong process and makes /health return 503 purely as a
/// function of how many tests ran before it.
/// </summary>
public class MemoryHealthCheckTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void Thresholds_DefaultToTheProductionValues_WhenNotConfigured()
    {
        var check = new MemoryHealthCheck(Config());

        Assert.Equal(MemoryHealthCheck.DefaultWarnMemoryMB, check.WarnMemoryMB);
        Assert.Equal(MemoryHealthCheck.DefaultMaxMemoryMB, check.MaxMemoryMB);
    }

    [Fact]
    public void Thresholds_ComeFromConfiguration_WhenProvided()
    {
        var check = new MemoryHealthCheck(Config(
            ("HealthChecks:MemoryWarnMB", "1111"),
            ("HealthChecks:MemoryMaxMB", "2222")));

        Assert.Equal(1111, check.WarnMemoryMB);
        Assert.Equal(2222, check.MaxMemoryMB);
    }

    [Fact]
    public async Task ReportsUnhealthy_WhenUsageExceedsTheConfiguredMaximum()
    {
        // Any live .NET process has a working set above 1MB.
        var check = new MemoryHealthCheck(Config(
            ("HealthChecks:MemoryWarnMB", "1"),
            ("HealthChecks:MemoryMaxMB", "1")));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task ReportsHealthy_WhenTheConfiguredMaximumIsAboveActualUsage()
    {
        var check = new MemoryHealthCheck(Config(
            ("HealthChecks:MemoryWarnMB", "1000000"),
            ("HealthChecks:MemoryMaxMB", "1000000")));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
