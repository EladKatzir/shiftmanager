namespace ShiftManager.Models.Telemetry;

/// <summary>
/// Core Web Vitals and performance metric entry.
/// Captures LCP, FID/INP, CLS, TTFB using the Performance API.
/// B-021: Real User Monitoring (LOCAL)
/// </summary>
public class PerformanceMetric
{
    /// <summary>
    /// Primary key
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Metric name: LCP (Largest Contentful Paint), FID (First Input Delay),
    /// INP (Interaction to Next Paint), CLS (Cumulative Layout Shift), TTFB (Time to First Byte)
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// Metric value (milliseconds for timing metrics, unitless for CLS)
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Rating based on Core Web Vitals thresholds: good, needs-improvement, poor
    /// </summary>
    public string? Rating { get; set; }

    /// <summary>
    /// Page URL where the metric was captured (path only)
    /// </summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Browser user agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Network connection type (4g, 3g, wifi, etc.)
    /// </summary>
    public string? ConnectionType { get; set; }

    /// <summary>
    /// Effective connection type from Network Information API
    /// </summary>
    public string? EffectiveType { get; set; }

    /// <summary>
    /// Device memory in GB (if available)
    /// </summary>
    public double? DeviceMemory { get; set; }

    /// <summary>
    /// Hardware concurrency (number of CPU cores)
    /// </summary>
    public int? HardwareConcurrency { get; set; }

    /// <summary>
    /// When the metric was captured (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Browser information (parsed from user agent)
    /// </summary>
    public string? BrowserInfo { get; set; }
}
