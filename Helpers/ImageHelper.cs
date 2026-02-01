namespace ShiftManager.Helpers;

/// <summary>
/// Helper class for image optimization utilities.
/// B-031: Image Optimization Audit
/// </summary>
public static class ImageHelper
{
    /// <summary>
    /// Common image widths for responsive srcset generation.
    /// </summary>
    public static readonly int[] StandardWidths = { 320, 640, 768, 1024, 1280, 1920 };

    /// <summary>
    /// Generates srcset attribute value for responsive images.
    /// </summary>
    /// <param name="basePath">The base image path (e.g., "/images/photo.jpg")</param>
    /// <param name="widths">Array of widths to generate (uses StandardWidths if null)</param>
    /// <returns>A srcset string like "photo-320w.jpg 320w, photo-640w.jpg 640w, ..."</returns>
    /// <example>
    /// var srcset = ImageHelper.GenerateSrcSet("/images/hero.jpg");
    /// // Returns: "/images/hero-320w.jpg 320w, /images/hero-640w.jpg 640w, ..."
    /// </example>
    public static string GenerateSrcSet(string basePath, int[]? widths = null)
    {
        if (string.IsNullOrEmpty(basePath))
            return string.Empty;

        widths ??= StandardWidths;

        var extension = Path.GetExtension(basePath);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
        var directory = Path.GetDirectoryName(basePath) ?? "";

        var srcset = widths.Select(w =>
        {
            var path = Path.Combine(directory, $"{nameWithoutExt}-{w}w{extension}").Replace("\\", "/");
            return $"{path} {w}w";
        });

        return string.Join(", ", srcset);
    }

    /// <summary>
    /// Generates a complete picture element HTML with WebP and fallback.
    /// </summary>
    /// <param name="src">Original image source path</param>
    /// <param name="alt">Alt text for the image</param>
    /// <param name="width">Image width in pixels</param>
    /// <param name="height">Image height in pixels</param>
    /// <param name="lazy">Whether to lazy load (default true)</param>
    /// <param name="cssClass">Optional CSS class for the img element</param>
    /// <returns>HTML string for picture element</returns>
    /// <example>
    /// @Html.Raw(ImageHelper.GeneratePictureHtml("/images/logo.png", "Company Logo", 200, 50, lazy: false))
    /// </example>
    public static string GeneratePictureHtml(
        string src,
        string alt,
        int width,
        int height,
        bool lazy = true,
        string? cssClass = null)
    {
        var webpSrc = ChangeExtension(src, ".webp");
        var lazyAttr = lazy ? "loading=\"lazy\" decoding=\"async\"" : "";
        var classAttr = !string.IsNullOrEmpty(cssClass) ? $"class=\"{cssClass}\"" : "";

        return $@"<picture>
    <source srcset=""{webpSrc}"" type=""image/webp"">
    <img src=""{src}"" alt=""{alt}"" width=""{width}"" height=""{height}"" {lazyAttr} {classAttr}>
</picture>".Trim();
    }

    /// <summary>
    /// Generates responsive picture element with srcset for multiple sizes.
    /// </summary>
    /// <param name="src">Original image source path</param>
    /// <param name="alt">Alt text for the image</param>
    /// <param name="width">Default/max image width</param>
    /// <param name="height">Default/max image height</param>
    /// <param name="sizes">Sizes attribute (e.g., "(max-width: 768px) 100vw, 50vw")</param>
    /// <param name="widths">Array of widths for srcset</param>
    /// <param name="lazy">Whether to lazy load</param>
    /// <returns>HTML string for responsive picture element</returns>
    public static string GenerateResponsivePictureHtml(
        string src,
        string alt,
        int width,
        int height,
        string sizes = "100vw",
        int[]? widths = null,
        bool lazy = true)
    {
        widths ??= StandardWidths;

        var srcSet = GenerateSrcSet(src, widths);
        var webpSrcSet = GenerateSrcSet(ChangeExtension(src, ".webp"), widths);
        var lazyAttr = lazy ? "loading=\"lazy\" decoding=\"async\"" : "";

        return $@"<picture>
    <source srcset=""{webpSrcSet}"" sizes=""{sizes}"" type=""image/webp"">
    <source srcset=""{srcSet}"" sizes=""{sizes}"">
    <img src=""{src}"" alt=""{alt}"" width=""{width}"" height=""{height}"" {lazyAttr}>
</picture>".Trim();
    }

    /// <summary>
    /// Generates an optimized img tag with proper attributes.
    /// </summary>
    /// <param name="src">Image source</param>
    /// <param name="alt">Alt text</param>
    /// <param name="width">Width in pixels</param>
    /// <param name="height">Height in pixels</param>
    /// <param name="lazy">Enable lazy loading</param>
    /// <param name="priority">Priority hint (auto, high, low)</param>
    /// <param name="cssClass">CSS class</param>
    /// <returns>HTML img tag</returns>
    public static string GenerateImgHtml(
        string src,
        string alt,
        int width,
        int height,
        bool lazy = true,
        string? priority = null,
        string? cssClass = null)
    {
        var attrs = new List<string>
        {
            $"src=\"{src}\"",
            $"alt=\"{alt}\"",
            $"width=\"{width}\"",
            $"height=\"{height}\""
        };

        if (lazy)
        {
            attrs.Add("loading=\"lazy\"");
            attrs.Add("decoding=\"async\"");
        }

        if (!string.IsNullOrEmpty(priority))
        {
            attrs.Add($"fetchpriority=\"{priority}\"");
        }

        if (!string.IsNullOrEmpty(cssClass))
        {
            attrs.Add($"class=\"{cssClass}\"");
        }

        return $"<img {string.Join(" ", attrs)}>";
    }

    /// <summary>
    /// Calculates aspect ratio CSS padding for responsive containers.
    /// </summary>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <returns>Padding-bottom percentage value</returns>
    /// <example>
    /// // For a 16:9 image (1920x1080):
    /// var padding = ImageHelper.CalculateAspectRatioPadding(1920, 1080);
    /// // Returns: 56.25
    /// </example>
    public static double CalculateAspectRatioPadding(int width, int height)
    {
        if (width <= 0 || height <= 0) return 100;
        return (double)height / width * 100;
    }

    /// <summary>
    /// Gets the aspect ratio as a string (e.g., "16 / 9").
    /// </summary>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <returns>CSS aspect-ratio value</returns>
    public static string GetAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0) return "1 / 1";

        // Try to find common aspect ratios
        var gcd = Gcd(width, height);
        var w = width / gcd;
        var h = height / gcd;

        // Common simplifications
        return (w, h) switch
        {
            (16, 9) or (1920, 1080) or (1280, 720) => "16 / 9",
            (4, 3) or (800, 600) or (1024, 768) => "4 / 3",
            (1, 1) => "1 / 1",
            (3, 2) or (1200, 800) => "3 / 2",
            (21, 9) => "21 / 9",
            _ => $"{w} / {h}"
        };
    }

    /// <summary>
    /// Changes the file extension of a path.
    /// </summary>
    private static string ChangeExtension(string path, string newExtension)
    {
        if (string.IsNullOrEmpty(path)) return path;

        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
            return path + newExtension;

        return path.Substring(0, path.Length - extension.Length) + newExtension;
    }

    /// <summary>
    /// Calculates the Greatest Common Divisor using Euclidean algorithm.
    /// </summary>
    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            var t = b;
            b = a % b;
            a = t;
        }
        return a;
    }

    /// <summary>
    /// Validates if image size is within recommended limits.
    /// </summary>
    /// <param name="fileSizeBytes">File size in bytes</param>
    /// <param name="maxSizeKb">Maximum allowed size in KB (default 100)</param>
    /// <returns>True if within limits</returns>
    public static bool IsWithinSizeLimit(long fileSizeBytes, int maxSizeKb = 100)
    {
        return fileSizeBytes <= maxSizeKb * 1024;
    }

    /// <summary>
    /// Gets a human-readable file size string.
    /// </summary>
    /// <param name="bytes">File size in bytes</param>
    /// <returns>Formatted string like "45.2 KB" or "1.5 MB"</returns>
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
