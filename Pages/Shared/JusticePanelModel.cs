namespace ShiftManager.Pages.Shared;

/// <summary>
/// Model for the <c>_JusticePanel.cshtml</c> partial. Used by every calendar page that wants
/// the in-context Justice drawer (Calendar/Shifts, Calendar/Chores, Calendar/OnCall as of Phase 2).
///
/// The host page constructs this once and passes it as the model. Most fields are pre-localized
/// strings so the partial doesn't need its own <see cref="IStringLocalizer"/> injection.
/// </summary>
public sealed class JusticePanelModel
{
    /// <summary>
    /// Discriminator used by the JS to know which calendar surfaced the drawer.
    /// One of: "shifts", "chores", "onduty". Drives the "Make it real" deep-link target later.
    /// </summary>
    public required string CalendarKind { get; init; }

    /// <summary>
    /// URL the JS XHR-fetches for the drawer payload. Should call the host page's
    /// <c>OnGetJusticeAsync</c> handler (e.g., <c>/Calendar/Shifts?handler=Justice</c>) with
    /// the current scope baked into the URL by the host page model.
    /// </summary>
    public required string DataEndpoint { get; init; }

    /// <summary>
    /// Title shown on the toolbar button's <c>title</c> attribute (already localized).
    /// </summary>
    public string? ButtonTitle { get; init; }

    /// <summary>
    /// Localized aria-label for the equity ribbon SVG-equivalent.
    /// </summary>
    public string? RibbonAria { get; init; }

    /// <summary>
    /// Localized close-button aria-label.
    /// </summary>
    public string? CloseLabel { get; init; }
}
