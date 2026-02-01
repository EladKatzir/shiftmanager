using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ShiftManager.TagHelpers;

/// <summary>
/// Tag helper for optimizing image loading and preventing CLS (Cumulative Layout Shift).
/// B-031: Image Optimization Audit
///
/// Usage:
///   Basic: <img optimize src="~/images/photo.jpg" alt="Description" width="300" height="200" />
///   No lazy load: <img optimize lazy="false" src="~/images/hero.jpg" alt="Hero" width="1200" height="600" />
/// </summary>
[HtmlTargetElement("img", Attributes = "optimize")]
public class OptimizedImageTagHelper : TagHelper
{
    private readonly ILogger<OptimizedImageTagHelper> _logger;

    public OptimizedImageTagHelper(ILogger<OptimizedImageTagHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enable optimization features for this image.
    /// </summary>
    [HtmlAttributeName("optimize")]
    public bool Optimize { get; set; }

    /// <summary>
    /// Enable lazy loading. Default is true.
    /// Set to false for above-the-fold images (hero images, logos).
    /// </summary>
    [HtmlAttributeName("lazy")]
    public bool Lazy { get; set; } = true;

    /// <summary>
    /// Priority hint for image loading.
    /// Set to "high" for LCP (Largest Contentful Paint) images.
    /// </summary>
    [HtmlAttributeName("priority")]
    public string? Priority { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!Optimize) return;

        var src = output.Attributes["src"]?.Value?.ToString();

        // Add loading="lazy" for below-fold images
        if (Lazy && !output.Attributes.ContainsName("loading"))
        {
            output.Attributes.SetAttribute("loading", "lazy");
        }

        // Add decoding="async" for non-blocking decode
        if (!output.Attributes.ContainsName("decoding"))
        {
            output.Attributes.SetAttribute("decoding", "async");
        }

        // Add fetchpriority for important images
        if (!string.IsNullOrEmpty(Priority) && !output.Attributes.ContainsName("fetchpriority"))
        {
            output.Attributes.SetAttribute("fetchpriority", Priority);
        }

        // Ensure width and height are set to prevent CLS
        var hasWidth = output.Attributes.ContainsName("width");
        var hasHeight = output.Attributes.ContainsName("height");

        if (!string.IsNullOrEmpty(src) && (!hasWidth || !hasHeight))
        {
            _logger.LogWarning(
                "Image optimization warning: {ImageSrc} is missing {MissingAttributes} attributes (CLS risk). " +
                "Add explicit width and height to prevent layout shift.",
                src,
                !hasWidth && !hasHeight ? "width and height" : (!hasWidth ? "width" : "height"));
        }
    }
}

/// <summary>
/// Tag helper for generating responsive picture elements with WebP fallback.
/// B-031: Image Optimization Audit
///
/// Usage:
///   <picture-responsive src="~/images/photo.jpg" alt="Description" width="800" height="600" />
///
/// Generates:
///   <picture>
///     <source srcset="~/images/photo.webp" type="image/webp">
///     <img src="~/images/photo.jpg" alt="Description" width="800" height="600" loading="lazy" decoding="async">
///   </picture>
/// </summary>
[HtmlTargetElement("picture-responsive", TagStructure = TagStructure.WithoutEndTag)]
public class PictureResponsiveTagHelper : TagHelper
{
    /// <summary>
    /// The source image path (PNG or JPG).
    /// </summary>
    [HtmlAttributeName("src")]
    public string Src { get; set; } = "";

    /// <summary>
    /// Alt text for the image (required for accessibility).
    /// </summary>
    [HtmlAttributeName("alt")]
    public string Alt { get; set; } = "";

    /// <summary>
    /// Width of the image in pixels.
    /// </summary>
    [HtmlAttributeName("width")]
    public int Width { get; set; }

    /// <summary>
    /// Height of the image in pixels.
    /// </summary>
    [HtmlAttributeName("height")]
    public int Height { get; set; }

    /// <summary>
    /// Enable lazy loading. Default is true.
    /// </summary>
    [HtmlAttributeName("lazy")]
    public bool Lazy { get; set; } = true;

    /// <summary>
    /// Additional CSS classes for the img element.
    /// </summary>
    [HtmlAttributeName("class")]
    public string? CssClass { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "picture";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Generate WebP source path
        var webpSrc = GenerateWebPPath(Src);
        var lazyAttrs = Lazy ? " loading=\"lazy\" decoding=\"async\"" : "";
        var classAttr = !string.IsNullOrEmpty(CssClass) ? $" class=\"{CssClass}\"" : "";

        output.Content.SetHtmlContent($@"
    <source srcset=""{webpSrc}"" type=""image/webp"">
    <img src=""{Src}"" alt=""{Alt}"" width=""{Width}"" height=""{Height}""{lazyAttrs}{classAttr}>");
    }

    private static string GenerateWebPPath(string originalPath)
    {
        if (string.IsNullOrEmpty(originalPath)) return originalPath;

        var extension = Path.GetExtension(originalPath);
        if (string.IsNullOrEmpty(extension)) return originalPath + ".webp";

        return originalPath.Substring(0, originalPath.Length - extension.Length) + ".webp";
    }
}

/// <summary>
/// Tag helper for avatar images with fallback initials.
/// B-031: Image Optimization Audit
///
/// Usage:
///   <avatar src="~/images/user.jpg" name="John Doe" size="md" />
///   <avatar name="John Doe" size="lg" /> (no image, shows initials)
/// </summary>
[HtmlTargetElement("avatar", TagStructure = TagStructure.WithoutEndTag)]
public class AvatarTagHelper : TagHelper
{
    /// <summary>
    /// The avatar image source. Optional - shows initials if not provided.
    /// </summary>
    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    /// <summary>
    /// The user's name. Used for alt text and generating initials.
    /// </summary>
    [HtmlAttributeName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Size variant: xs (24px), sm (32px), md (40px), lg (56px), xl (80px).
    /// </summary>
    [HtmlAttributeName("size")]
    public string Size { get; set; } = "md";

    /// <summary>
    /// Additional CSS classes.
    /// </summary>
    [HtmlAttributeName("class")]
    public string? CssClass { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var sizeClass = Size.ToLower() switch
        {
            "xs" => "avatar--xs",
            "sm" => "avatar--sm",
            "md" => "",
            "lg" => "avatar--lg",
            "xl" => "avatar--xl",
            _ => ""
        };

        var classes = $"avatar {sizeClass}".Trim();
        if (!string.IsNullOrEmpty(CssClass))
        {
            classes += $" {CssClass}";
        }

        output.Attributes.SetAttribute("class", classes);
        output.Attributes.SetAttribute("role", "img");
        output.Attributes.SetAttribute("aria-label", Name);

        if (!string.IsNullOrEmpty(Src))
        {
            // Get dimensions based on size
            var (width, height) = GetDimensions(Size);
            output.Content.SetHtmlContent(
                $"<img src=\"{Src}\" alt=\"{Name}\" width=\"{width}\" height=\"{height}\" loading=\"lazy\" decoding=\"async\">");
        }
        else
        {
            // Show initials
            var initials = GetInitials(Name);
            output.Content.SetHtmlContent($"<span class=\"avatar__fallback\">{initials}</span>");
        }
    }

    private static (int width, int height) GetDimensions(string size)
    {
        return size.ToLower() switch
        {
            "xs" => (24, 24),
            "sm" => (32, 32),
            "md" => (40, 40),
            "lg" => (56, 56),
            "xl" => (80, 80),
            _ => (40, 40)
        };
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";

        if (parts.Length == 1)
        {
            return parts[0].Length >= 2
                ? parts[0].Substring(0, 2).ToUpper()
                : parts[0].ToUpper();
        }

        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }
}
