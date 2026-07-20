using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ShiftManager.TagHelpers;

/// <summary>
/// Renders an SVG icon from the icon system.
/// Usage: &lt;icon name="calendar" size="md" /&gt;
/// </summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public class IconTagHelper : TagHelper
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        // Navigation
        ["home"] = "<path d=\"M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6\"/>",
        ["calendar"] = "<path d=\"M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z\"/>",
        ["calendar-days"] = "<path d=\"M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z\"/><path d=\"M8 14h.01M12 14h.01M16 14h.01M8 18h.01M12 18h.01\"/>",
        ["calendar-check"] = "<path d=\"M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z\"/><path d=\"M9 16l2 2 4-4\"/>",
        ["calendar-x"] = "<path d=\"M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z\"/><path d=\"M10 14l4 4m0-4l-4 4\"/>",
        ["calendar-plus"] = "<path d=\"M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z\"/><path d=\"M12 14v4m-2-2h4\"/>",
        ["clock"] = "<path d=\"M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z\"/>",
        ["timer"] = "<path d=\"M12 6v6l4 2\"/><circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M10 2h4\"/>",

        // People
        ["user"] = "<path d=\"M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z\"/>",
        ["users"] = "<path d=\"M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z\"/>",
        ["users-round"] = "<circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M3 21v-2a4 4 0 014-4h4a4 4 0 014 4v2\"/><circle cx=\"17\" cy=\"7\" r=\"3\"/><path d=\"M21 21v-2a3 3 0 00-3-3h-1\"/>",
        ["user-plus"] = "<path d=\"M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14M19 8v6m3-3h-6\"/>",
        ["user-minus"] = "<path d=\"M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14M16 11h6\"/>",
        ["user-check"] = "<path d=\"M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14M16 11l2 2 4-4\"/>",
        ["user-x"] = "<path d=\"M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14\"/><line x1=\"17\" y1=\"8\" x2=\"22\" y2=\"13\"/><line x1=\"22\" y1=\"8\" x2=\"17\" y2=\"13\"/>",
        ["contact"] = "<path d=\"M17 18a2 2 0 00-2-2H9a2 2 0 00-2 2\"/><rect width=\"18\" height=\"18\" x=\"3\" y=\"4\" rx=\"2\"/><circle cx=\"12\" cy=\"10\" r=\"2\"/><line x1=\"8\" x2=\"8\" y1=\"2\" y2=\"4\"/><line x1=\"16\" x2=\"16\" y1=\"2\" y2=\"4\"/>",

        // Actions
        ["check"] = "<path d=\"M5 13l4 4L19 7\"/>",
        ["x"] = "<path d=\"M6 18L18 6M6 6l12 12\"/>",
        ["plus"] = "<path d=\"M12 4v16m8-8H4\"/>",
        ["plus-circle"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M8 12h8\"/><path d=\"M12 8v8\"/>",
        ["minus"] = "<path d=\"M20 12H4\"/>",
        ["edit"] = "<path d=\"M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z\"/>",
        ["pencil"] = "<path d=\"M17 3a2.85 2.83 0 114 4L7.5 20.5 2 22l1.5-5.5Z\"/>",
        ["trash"] = "<path d=\"M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16\"/>",
        ["trash-2"] = "<path d=\"M3 6h18M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2m3 0v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6h14zM10 11v6M14 11v6\"/>",
        ["save"] = "<path d=\"M19 21H5a2 2 0 01-2-2V5a2 2 0 012-2h11l5 5v11a2 2 0 01-2 2z\"/><polyline points=\"17 21 17 13 7 13 7 21\"/><polyline points=\"7 3 7 8 15 8\"/>",
        ["search"] = "<path d=\"M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z\"/>",
        ["filter"] = "<path d=\"M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z\"/>",
        ["download"] = "<path d=\"M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4\"/>",
        ["upload"] = "<path d=\"M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12\"/>",
        ["refresh"] = "<path d=\"M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15\"/>",
        ["refresh-cw"] = "<path d=\"M21 2v6h-6M3 12a9 9 0 0115-6.7L21 8M3 22v-6h6M21 12a9 9 0 01-15 6.7L3 16\"/>",
        ["copy"] = "<rect x=\"9\" y=\"9\" width=\"13\" height=\"13\" rx=\"2\" ry=\"2\"/><path d=\"M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1\"/>",

        // Navigation Controls
        ["chevron-down"] = "<path d=\"M19 9l-7 7-7-7\"/>",
        ["chevron-up"] = "<path d=\"M5 15l7-7 7 7\"/>",
        ["chevron-right"] = "<path d=\"M9 5l7 7-7 7\"/>",
        ["chevron-left"] = "<path d=\"M15 19l-7-7 7-7\"/>",
        ["arrow-left"] = "<path d=\"M19 12H5M12 19l-7-7 7-7\"/>",
        ["arrow-right"] = "<path d=\"M5 12h14M12 5l7 7-7 7\"/>",
        ["menu"] = "<path d=\"M4 6h16M4 12h16M4 18h16\"/>",
        ["more-vertical"] = "<circle cx=\"12\" cy=\"12\" r=\"1\"/><circle cx=\"12\" cy=\"5\" r=\"1\"/><circle cx=\"12\" cy=\"19\" r=\"1\"/>",
        ["more-horizontal"] = "<circle cx=\"12\" cy=\"12\" r=\"1\"/><circle cx=\"5\" cy=\"12\" r=\"1\"/><circle cx=\"19\" cy=\"12\" r=\"1\"/>",
        ["external-link"] = "<path d=\"M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3\"/>",
        ["link"] = "<path d=\"M10 13a5 5 0 007.54.54l3-3a5 5 0 00-7.07-7.07l-1.72 1.71\"/><path d=\"M14 11a5 5 0 00-7.54-.54l-3 3a5 5 0 007.07 7.07l1.71-1.71\"/>",

        // Status
        ["alert-circle"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"16\" x2=\"12.01\" y2=\"16\"/>",
        ["alert-triangle"] = "<path d=\"M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z\"/>",
        ["check-circle"] = "<path d=\"M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z\"/>",
        ["info"] = "<path d=\"M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z\"/>",
        ["help-circle"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M9.09 9a3 3 0 015.83 1c0 2-3 3-3 3\"/><line x1=\"12\" y1=\"17\" x2=\"12.01\" y2=\"17\"/>",
        ["x-circle"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M15 9l-6 6M9 9l6 6\"/>",
        ["warning"] = "<path d=\"M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z\"/>",
        ["error"] = "<path d=\"M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z\"/>",
        ["success"] = "<path d=\"M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z\"/>",

        // Communication
        ["bell"] = "<path d=\"M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9\"/>",
        ["mail"] = "<path d=\"M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z\"/>",
        ["phone"] = "<path d=\"M3 5a2 2 0 012-2h3.28a1 1 0 01.948.684l1.498 4.493a1 1 0 01-.502 1.21l-2.257 1.13a11.042 11.042 0 005.516 5.516l1.13-2.257a1 1 0 011.21-.502l4.493 1.498a1 1 0 01.684.949V19a2 2 0 01-2 2h-1C9.716 21 3 14.284 3 6V5z\"/>",
        ["phone-off"] = "<path d=\"M10.68 13.31a16 16 0 003.41 2.6l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7 2 2 0 011.72 2v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.42 19.42 0 01-6-6 19.79 19.79 0 01-3.07-8.63A2 2 0 014.11 2h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L8.09 9.91\"/><line x1=\"22\" y1=\"2\" x2=\"2\" y2=\"22\"/>",
        ["phone-incoming"] = "<polyline points=\"16 2 16 8 22 8\"/><line x1=\"22\" y1=\"2\" x2=\"16\" y2=\"8\"/><path d=\"M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7A2 2 0 0122 16.92z\"/>",
        ["message-square"] = "<path d=\"M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z\"/>",

        // Files & Data
        ["file"] = "<path d=\"M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z\"/><polyline points=\"14 2 14 8 20 8\"/>",
        ["file-text"] = "<path d=\"M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z\"/><polyline points=\"14 2 14 8 20 8\"/><line x1=\"16\" y1=\"13\" x2=\"8\" y2=\"13\"/><line x1=\"16\" y1=\"17\" x2=\"8\" y2=\"17\"/><line x1=\"10\" y1=\"9\" x2=\"8\" y2=\"9\"/>",
        ["folder"] = "<path d=\"M22 19a2 2 0 01-2 2H4a2 2 0 01-2-2V5a2 2 0 012-2h5l2 3h9a2 2 0 012 2z\"/>",
        ["database"] = "<ellipse cx=\"12\" cy=\"5\" rx=\"9\" ry=\"3\"/><path d=\"M21 12c0 1.66-4 3-9 3s-9-1.34-9-3\"/><path d=\"M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5\"/>",
        ["bar-chart-2"] = "<path d=\"M18 20V10M12 20V4M6 20v-6\"/>",
        ["pie-chart"] = "<path d=\"M21.21 15.89A10 10 0 118 2.83\"/><path d=\"M22 12A10 10 0 0012 2v10z\"/>",

        // Buildings & Organization
        // Real Lucide "building" — single tower with windows. Distinct from building-2 (office complex).
        ["building"] = "<rect width=\"16\" height=\"20\" x=\"4\" y=\"2\" rx=\"2\" ry=\"2\"/><path d=\"M9 22v-4h6v4\"/><path d=\"M8 6h.01\"/><path d=\"M16 6h.01\"/><path d=\"M12 6h.01\"/><path d=\"M12 10h.01\"/><path d=\"M12 14h.01\"/><path d=\"M16 10h.01\"/><path d=\"M16 14h.01\"/><path d=\"M8 10h.01\"/><path d=\"M8 14h.01\"/>",
        ["building-2"] = "<path d=\"M6 22V4a2 2 0 012-2h8a2 2 0 012 2v18Z\"/><path d=\"M6 12H4a2 2 0 00-2 2v6a2 2 0 002 2h2\"/><path d=\"M18 9h2a2 2 0 012 2v9a2 2 0 01-2 2h-2\"/><path d=\"M10 6h4\"/><path d=\"M10 10h4\"/><path d=\"M10 14h4\"/><path d=\"M10 18h4\"/>",
        ["briefcase"] = "<rect x=\"2\" y=\"7\" width=\"20\" height=\"14\" rx=\"2\" ry=\"2\"/><path d=\"M16 21V5a2 2 0 00-2-2h-4a2 2 0 00-2 2v16\"/>",
        ["layout-dashboard"] = "<rect x=\"3\" y=\"3\" width=\"7\" height=\"9\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"5\"/><rect x=\"14\" y=\"12\" width=\"7\" height=\"9\"/><rect x=\"3\" y=\"16\" width=\"7\" height=\"5\"/>",

        // Security & Auth
        ["lock"] = "<rect x=\"3\" y=\"11\" width=\"18\" height=\"11\" rx=\"2\" ry=\"2\"/><path d=\"M7 11V7a5 5 0 0110 0v4\"/>",
        ["unlock"] = "<rect x=\"3\" y=\"11\" width=\"18\" height=\"11\" rx=\"2\" ry=\"2\"/><path d=\"M7 11V7a5 5 0 019.9-1\"/>",
        ["shield"] = "<path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\"/>",
        ["key"] = "<path d=\"M21 2l-2 2m-7.61 7.61a5.5 5.5 0 11-7.778 7.778 5.5 5.5 0 017.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4\"/>",
        ["log-out"] = "<path d=\"M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1\"/>",
        ["log-in"] = "<path d=\"M15 3h4a2 2 0 012 2v14a2 2 0 01-2 2h-4M10 17l5-5-5-5M15 12H3\"/>",

        // Settings & Config
        ["settings"] = "<path d=\"M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z\"/><path d=\"M15 12a3 3 0 11-6 0 3 3 0 016 0z\"/>",

        // Misc
        ["star"] = "<path d=\"M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z\"/>",
        ["heart"] = "<path d=\"M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z\"/>",
        ["flag"] = "<path d=\"M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z\"/><line x1=\"4\" y1=\"22\" x2=\"4\" y2=\"15\"/>",
        ["tag"] = "<path d=\"M20.59 13.41l-7.17 7.17a2 2 0 01-2.83 0L2 12V2h10l8.59 8.59a2 2 0 010 2.82z\"/><line x1=\"7\" y1=\"7\" x2=\"7.01\" y2=\"7\"/>",
        ["map-pin"] = "<path d=\"M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z\"/><circle cx=\"12\" cy=\"10\" r=\"3\"/>",
        ["globe"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"2\" y1=\"12\" x2=\"22\" y2=\"12\"/><path d=\"M12 2a15.3 15.3 0 014 10 15.3 15.3 0 01-4 10 15.3 15.3 0 01-4-10 15.3 15.3 0 014-10z\"/>",
        ["sun"] = "<circle cx=\"12\" cy=\"12\" r=\"5\"/><line x1=\"12\" y1=\"1\" x2=\"12\" y2=\"3\"/><line x1=\"12\" y1=\"21\" x2=\"12\" y2=\"23\"/><line x1=\"4.22\" y1=\"4.22\" x2=\"5.64\" y2=\"5.64\"/><line x1=\"18.36\" y1=\"18.36\" x2=\"19.78\" y2=\"19.78\"/><line x1=\"1\" y1=\"12\" x2=\"3\" y2=\"12\"/><line x1=\"21\" y1=\"12\" x2=\"23\" y2=\"12\"/><line x1=\"4.22\" y1=\"19.78\" x2=\"5.64\" y2=\"18.36\"/><line x1=\"18.36\" y1=\"5.64\" x2=\"19.78\" y2=\"4.22\"/>",
        ["moon"] = "<path d=\"M21 12.79A9 9 0 1111.21 3 7 7 0 0021 12.79z\"/>",
        ["eye"] = "<path d=\"M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
        ["eye-off"] = "<path d=\"M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24\"/><line x1=\"1\" y1=\"1\" x2=\"23\" y2=\"23\"/>",
        ["undo"] = "<path d=\"M3 7v6h6\"/><path d=\"M21 17a9 9 0 00-9-9 9 9 0 00-6 2.3L3 13\"/>",
        ["redo"] = "<path d=\"M21 7v6h-6\"/><path d=\"M3 17a9 9 0 019-9 9 9 0 016 2.3L21 13\"/>",

        // Extended mapping (added 2026-04-16 for Phase 2/3 follow-up)
        // Real Lucide "sparkles" path. Three sparkle glyphs, all within 0..24 viewBox.
        ["sparkles"] = "<path d=\"M9.937 15.5A2 2 0 008.5 14.063l-6.135-1.582a.5.5 0 010-.962L8.5 9.936A2 2 0 009.937 8.5l1.582-6.135a.5.5 0 01.963 0L14.063 8.5A2 2 0 0015.5 9.937l6.135 1.582a.5.5 0 010 .962L15.5 14.063a2 2 0 00-1.437 1.437l-1.582 6.135a.5.5 0 01-.963 0z\"/><path d=\"M20 3v4\"/><path d=\"M22 5h-4\"/><path d=\"M4 17v2\"/><path d=\"M5 18H3\"/>",
        ["target"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><circle cx=\"12\" cy=\"12\" r=\"6\"/><circle cx=\"12\" cy=\"12\" r=\"2\"/>",
        ["clipboard-list"] = "<rect x=\"8\" y=\"2\" width=\"8\" height=\"4\" rx=\"1\" ry=\"1\"/><path d=\"M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2\"/><line x1=\"8\" y1=\"10\" x2=\"16\" y2=\"10\"/><line x1=\"8\" y1=\"14\" x2=\"16\" y2=\"14\"/><line x1=\"8\" y1=\"18\" x2=\"12\" y2=\"18\"/>",
        ["scroll-text"] = "<path d=\"M15 12h-5\"/><path d=\"M15 8h-5\"/><path d=\"M19 17V5a2 2 0 00-2-2H4\"/><path d=\"M8 21h12a2 2 0 002-2v-1a1 1 0 00-1-1H11a1 1 0 00-1 1v1a2 2 0 11-4 0V5a2 2 0 10-4 0v2a1 1 0 001 1h3\"/>",
        ["alarm-clock"] = "<circle cx=\"12\" cy=\"13\" r=\"8\"/><path d=\"M12 9v4l2 2\"/><path d=\"M5 3L2 6\"/><path d=\"M22 6l-3-3\"/><path d=\"M6.38 18.7L4 21\"/><path d=\"M17.64 18.67L20 21\"/>",
        ["palm-tree"] = "<path d=\"M13 8c0-2.76-2.46-5-5.5-5S2 5.24 2 8h2l1-1 1 1h4\"/><path d=\"M13 7.14A5.82 5.82 0 0116.5 6c3.04 0 5.5 2.24 5.5 5h-3l-1-1-1 1h-3\"/><path d=\"M5.89 9.71c-2.15 2.15-2.3 5.47-.35 7.43l4.24-4.25.7-.7.71-.71 2.12-2.12c-1.95-1.96-5.27-1.8-7.42.35z\"/><path d=\"M11 15.5c.5 2.5-.17 4.5-1 6.5h4c2-5.5-.5-12-1-14\"/>",
        ["folder-tree"] = "<path d=\"M20 10a1 1 0 001-1V6a1 1 0 00-1-1h-2.5a1 1 0 01-.8-.4l-.9-1.2A1 1 0 0015 3h-2a1 1 0 00-1 1v5a1 1 0 001 1z\"/><path d=\"M20 21a1 1 0 001-1v-3a1 1 0 00-1-1h-2.9a1 1 0 01-.88-.55l-.42-.85a1 1 0 00-.92-.6H13a1 1 0 00-1 1v5a1 1 0 001 1z\"/><path d=\"M3 5a2 2 0 002 2h3\"/><path d=\"M3 3v13a2 2 0 002 2h3\"/>",
        ["bar-chart-3"] = "<path d=\"M3 3v18h18\"/><path d=\"M18 17V9\"/><path d=\"M13 17V5\"/><path d=\"M8 17v-3\"/>",
        ["zap"] = "<polygon points=\"13 2 3 14 12 14 11 22 21 10 12 10 13 2\"/>",
        ["palette"] = "<circle cx=\"13.5\" cy=\"6.5\" r=\".5\"/><circle cx=\"17.5\" cy=\"10.5\" r=\".5\"/><circle cx=\"8.5\" cy=\"7.5\" r=\".5\"/><circle cx=\"6.5\" cy=\"12.5\" r=\".5\"/><path d=\"M12 2a10 10 0 0010 10 4 4 0 01-4 4h-1.5a2 2 0 000 4 2 2 0 01-2 2 10 10 0 110-20z\"/>",
        ["shield-check"] = "<path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\"/><path d=\"M9 12l2 2 4-4\"/>",
        ["inbox"] = "<polyline points=\"22 12 16 12 14 15 10 15 8 12 2 12\"/><path d=\"M5.45 5.11L2 12v6a2 2 0 002 2h16a2 2 0 002-2v-6l-3.45-6.89A2 2 0 0016.76 4H7.24a2 2 0 00-1.79 1.11z\"/>",
        ["keyboard"] = "<rect x=\"2\" y=\"4\" width=\"20\" height=\"16\" rx=\"2\" ry=\"2\"/><path d=\"M6 8h.01M10 8h.01M14 8h.01M18 8h.01M8 12h.01M12 12h.01M16 12h.01M7 16h10\"/>",
        ["radio"] = "<circle cx=\"12\" cy=\"12\" r=\"2\"/><path d=\"M16.24 7.76a6 6 0 010 8.49m-8.48-.01a6 6 0 010-8.49m11.31-2.82a10 10 0 010 14.14m-14.14 0a10 10 0 010-14.14\"/>",
        ["megaphone"] = "<path d=\"M3 11l18-5v12L3 14v-3z\"/><path d=\"M11.6 16.8a3 3 0 11-5.8-1.6\"/>",
        ["pin"] = "<line x1=\"12\" y1=\"17\" x2=\"12\" y2=\"22\"/><path d=\"M5 17h14v-1.76a2 2 0 00-1.11-1.79l-1.78-.9A2 2 0 0115 10.76V6h1a2 2 0 002-2V3H6v1a2 2 0 002 2h1v4.76a2 2 0 01-1.11 1.79l-1.78.9A2 2 0 005 15.24V17z\"/>",
        ["medal"] = "<path d=\"M7.21 15l-2.88 5.7a.5.5 0 00.61.7l2.59-.9a.5.5 0 01.58.2l1.56 2.2a.5.5 0 00.9-.1l2.4-7.8\"/><path d=\"M16.79 15l2.88 5.7a.5.5 0 01-.61.7l-2.59-.9a.5.5 0 00-.58.2l-1.56 2.2a.5.5 0 01-.9-.1l-2.4-7.8\"/><circle cx=\"12\" cy=\"8\" r=\"6\"/>",
        ["sliders"] = "<line x1=\"4\" y1=\"21\" x2=\"4\" y2=\"14\"/><line x1=\"4\" y1=\"10\" x2=\"4\" y2=\"3\"/><line x1=\"12\" y1=\"21\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"3\"/><line x1=\"20\" y1=\"21\" x2=\"20\" y2=\"16\"/><line x1=\"20\" y1=\"12\" x2=\"20\" y2=\"3\"/><line x1=\"1\" y1=\"14\" x2=\"7\" y2=\"14\"/><line x1=\"9\" y1=\"8\" x2=\"15\" y2=\"8\"/><line x1=\"17\" y1=\"16\" x2=\"23\" y2=\"16\"/>",
        ["book-open"] = "<path d=\"M2 3h6a4 4 0 014 4v14a3 3 0 00-3-3H2z\"/><path d=\"M22 3h-6a4 4 0 00-4 4v14a3 3 0 013-3h7z\"/>",
        ["store"] = "<path d=\"M3 9h18l-2 11H5L3 9z\"/><path d=\"M3 9l1-6h16l1 6\"/><path d=\"M12 12v4\"/>",
        ["landmark"] = "<line x1=\"3\" y1=\"22\" x2=\"21\" y2=\"22\"/><line x1=\"6\" y1=\"18\" x2=\"6\" y2=\"11\"/><line x1=\"10\" y1=\"18\" x2=\"10\" y2=\"11\"/><line x1=\"14\" y1=\"18\" x2=\"14\" y2=\"11\"/><line x1=\"18\" y1=\"18\" x2=\"18\" y2=\"11\"/><polygon points=\"12 2 20 7 4 7\"/>",
        ["hammer"] = "<path d=\"M15 12l-8.5 8.5a2.12 2.12 0 01-3-3L12 9\"/><path d=\"M17.64 15L22 10.64\"/><path d=\"M20.91 11.7l-1.25-1.25c-.6-.6-.93-1.4-.93-2.25v-.86L16.01 4.6a5.56 5.56 0 00-3.94-1.64H9l.92.82A6.18 6.18 0 0112 8.4v1.56l2 2h2.47l2.26 1.91\"/>",
        ["trending-up"] = "<polyline points=\"23 6 13.5 15.5 8.5 10.5 1 18\"/><polyline points=\"17 6 23 6 23 12\"/>",
        ["party-popper"] = "<path d=\"M5.8 11.3L2 22l10.7-3.79\"/><path d=\"M4 3h.01\"/><path d=\"M22 8h.01\"/><path d=\"M15 2h.01\"/><path d=\"M22 20h.01\"/><path d=\"M22 2l-2.24.75a2.9 2.9 0 00-1.96 3.12c.1.86-.57 1.63-1.45 1.63h-.38c-.86 0-1.6.6-1.76 1.44L14 10\"/>",
        ["siren"] = "<path d=\"M7 12a5 5 0 0110 0v7H7v-7z\"/><path d=\"M5 20a2 2 0 012-2h10a2 2 0 012 2v2H5v-2z\"/><path d=\"M4 12c0-4 3-8 8-8\"/><path d=\"M20 12c0-4-3-8-8-8\"/>",
        ["construction"] = "<rect x=\"2\" y=\"6\" width=\"20\" height=\"8\" rx=\"1\"/><path d=\"M17 14v7\"/><path d=\"M7 14v7\"/><path d=\"M17 3v3\"/><path d=\"M7 3v3\"/><path d=\"M10 14 2.3 6.3\"/><path d=\"M14 6 21.7 13.7\"/>",
        ["ban"] = "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M4.93 4.93l14.14 14.14\"/>",
        ["printer"] = "<polyline points=\"6 9 6 2 18 2 18 9\"/><path d=\"M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2\"/><rect x=\"6\" y=\"14\" width=\"12\" height=\"8\"/>",

        // Added 2026-04-17 alongside the emoji→Lucide migration of chore/duty/timeline views.
        ["brush"] = "<path d=\"m9.06 11.9 8.07-8.06a2.85 2.85 0 1 1 4.03 4.03l-8.06 8.08\"/><path d=\"M7.07 14.94c-1.66 0-3 1.35-3 3.02 0 1.33-2.5 1.52-2 2.02 1.08 1.1 2.49 2.02 4 2.02 2.2 0 4-1.8 4-4.04a3.01 3.01 0 0 0-3-3.02z\"/>",
        ["gauge"] = "<path d=\"m12 14 4-4\"/><path d=\"M3.34 19a10 10 0 1 1 17.32 0\"/>",
        ["layers"] = "<polygon points=\"12 2 2 7 12 12 22 7 12 2\"/><polyline points=\"2 17 12 22 22 17\"/><polyline points=\"2 12 12 17 22 12\"/>",
        ["table"] = "<rect width=\"18\" height=\"18\" x=\"3\" y=\"3\" rx=\"2\"/><path d=\"M3 9h18\"/><path d=\"M3 15h18\"/><path d=\"M9 3v18\"/><path d=\"M15 3v18\"/>",
        ["bell-ring"] = "<path d=\"M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9\"/><path d=\"M10.3 21a1.94 1.94 0 0 0 3.4 0\"/><path d=\"M4 2C2.8 3.7 2 5.7 2 8\"/><path d=\"M22 8c0-2.3-.8-4.3-2-6\"/>",
        ["git-branch"] = "<line x1=\"6\" x2=\"6\" y1=\"3\" y2=\"15\"/><circle cx=\"18\" cy=\"6\" r=\"3\"/><circle cx=\"6\" cy=\"18\" r=\"3\"/><path d=\"M18 9a9 9 0 0 1-9 9\"/>",
        ["toggle-left"] = "<rect width=\"20\" height=\"12\" x=\"2\" y=\"6\" rx=\"6\" ry=\"6\"/><circle cx=\"8\" cy=\"12\" r=\"2\"/>",
        ["activity"] = "<path d=\"M22 12h-4l-3 9L9 3l-3 9H2\"/>",
        ["heart-pulse"] = "<path d=\"M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z\"/><path d=\"M3.22 12H9.5l.5-1 2 4.5 2-7 1.5 3.5h5.27\"/>",
        ["heart-handshake"] = "<path d=\"M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z\"/><path d=\"M12 5 9.04 7.96a2.17 2.17 0 0 0 0 3.08c.82.82 2.13.85 3 .07l2.07-1.9a2.82 2.82 0 0 1 3.79 0l2.96 2.66\"/><path d=\"m18 15-2-2\"/><path d=\"m15 18-2-2\"/>",

        // Added 2026-05-03 for HOME unification (Task 22) — chip composition source icons.
        // Lucide canonical paths.
        // "house" — rotation HOME default + always-present home glyph (distinct from "home" which uses an older Heroicons-style path).
        ["house"] = "<path d=\"M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8\"/><path d=\"M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z\"/>",
        // "plane" — Lucide plane (vacation HOME source icon).
        ["plane"] = "<path d=\"M17.8 19.2 16 11l3.5-3.5C21 6 21.5 4 21 3c-1-.5-3 0-4.5 1.5L13 8 4.8 6.2c-.5-.1-.9.1-1.1.5l-.3.5c-.2.5-.1 1 .3 1.3L9 12l-2 3H4l-1 1 3 2 2 3 1-1v-3l3-2 3.5 5.3c.3.4.8.5 1.3.3l.5-.2c.4-.3.6-.7.5-1.2z\"/>",
        // "sunrise" — Lucide sunrise (after HOME source icon).
        ["sunrise"] = "<path d=\"M12 2v8\"/><path d=\"m4.93 10.93 1.41 1.41\"/><path d=\"M2 18h2\"/><path d=\"M20 18h2\"/><path d=\"m19.07 10.93-1.41 1.41\"/><path d=\"M22 22H2\"/><path d=\"m8 6 4-4 4 4\"/><path d=\"M16 18a4 4 0 0 0-8 0\"/>",
        // "repeat" — Lucide repeat (rotation HOME source icon).
        ["repeat"] = "<path d=\"m17 2 4 4-4 4\"/><path d=\"M3 11v-1a4 4 0 0 1 4-4h14\"/><path d=\"m7 22-4-4 4-4\"/><path d=\"M21 13v1a4 4 0 0 1-4 4H3\"/>"
    };

    private static readonly Dictionary<string, int> Sizes = new()
    {
        ["xs"] = 12,
        ["sm"] = 16,
        ["md"] = 20,
        ["lg"] = 24,
        ["xl"] = 32,
        ["2xl"] = 48
    };

    /// <summary>
    /// The name of the icon (e.g., "home", "calendar", "users").
    /// See the Icons dictionary for available icons.
    /// </summary>
    [HtmlAttributeName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Size of the icon. Can be:
    /// - A size token: "xs" (12px), "sm" (16px), "md" (20px), "lg" (24px), "xl" (32px), "2xl" (48px)
    /// - A number: treated as pixel size (e.g., "18" = 18px)
    /// Default is "md" (20px).
    /// </summary>
    [HtmlAttributeName("size")]
    public string Size { get; set; } = "md";

    /// <summary>
    /// Additional CSS classes to apply to the icon.
    /// </summary>
    [HtmlAttributeName("class")]
    public string CssClass { get; set; } = "";

    /// <summary>
    /// Accessible label for the icon. If not set, the icon will be decorative (aria-hidden="true").
    /// </summary>
    [HtmlAttributeName("aria-label")]
    public string? AriaLabel { get; set; }

    /// <summary>
    /// Stroke width for the icon. Default is 2.
    /// </summary>
    [HtmlAttributeName("stroke-width")]
    public double StrokeWidth { get; set; } = 2;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var iconName = Name.ToLowerInvariant();

        if (!Icons.TryGetValue(iconName, out var path))
        {
            // Fallback for unknown icons - render placeholder text
            output.TagName = "span";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Attributes.SetAttribute("class", $"icon icon--unknown {CssClass}".Trim());
            output.Attributes.SetAttribute("title", $"Unknown icon: {Name}");
            output.Content.SetContent($"[{Name}]");
            return;
        }

        // Determine pixel size from token or numeric value
        int size;
        if (Sizes.TryGetValue(Size.ToLowerInvariant(), out var tokenSize))
        {
            size = tokenSize;
        }
        else if (int.TryParse(Size, out var numericSize))
        {
            size = numericSize;
        }
        else
        {
            size = 20; // Default to md
        }

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("width", size);
        output.Attributes.SetAttribute("height", size);
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        output.Attributes.SetAttribute("fill", "none");
        output.Attributes.SetAttribute("stroke", "currentColor");
        output.Attributes.SetAttribute("stroke-width", StrokeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        output.Attributes.SetAttribute("stroke-linecap", "round");
        output.Attributes.SetAttribute("stroke-linejoin", "round");
        output.Attributes.SetAttribute("class", $"icon icon--{iconName} {CssClass}".Trim());

        if (!string.IsNullOrEmpty(AriaLabel))
        {
            output.Attributes.SetAttribute("aria-label", AriaLabel);
            output.Attributes.SetAttribute("role", "img");
        }
        else
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
        }

        output.Content.SetHtmlContent(path);
    }
}

/// <summary>
/// Mapping of semantic names to icon names for common UI elements.
/// This helps maintain consistency and allows easy updates if icons change.
/// </summary>
public static class IconNames
{
    // Navigation
    public const string Home = "home";
    public const string Calendar = "calendar";
    public const string CalendarDays = "calendar-days";
    public const string Users = "users";
    public const string User = "user";
    public const string Settings = "settings";
    public const string Building = "building";
    public const string Building2 = "building-2";
    public const string Briefcase = "briefcase";
    public const string LayoutDashboard = "layout-dashboard";

    // Actions
    public const string Plus = "plus";
    public const string Minus = "minus";
    public const string Edit = "pencil";
    public const string Delete = "trash-2";
    public const string Save = "save";
    public const string Close = "x";
    public const string Check = "check";
    public const string Search = "search";
    public const string Filter = "filter";
    public const string Download = "download";
    public const string Upload = "upload";
    public const string Print = "printer";
    public const string Refresh = "refresh-cw";
    public const string Copy = "copy";

    // Navigation Controls
    public const string ChevronDown = "chevron-down";
    public const string ChevronUp = "chevron-up";
    public const string ChevronLeft = "chevron-left";
    public const string ChevronRight = "chevron-right";
    public const string ArrowLeft = "arrow-left";
    public const string ArrowRight = "arrow-right";
    public const string Menu = "menu";
    public const string MoreVertical = "more-vertical";
    public const string MoreHorizontal = "more-horizontal";
    public const string ExternalLink = "external-link";
    public const string Link = "link";

    // Status
    public const string AlertCircle = "alert-circle";
    public const string AlertTriangle = "alert-triangle";
    public const string CheckCircle = "check-circle";
    public const string Info = "info";
    public const string HelpCircle = "help-circle";
    public const string XCircle = "x-circle";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Success = "success";

    // Communication
    public const string Bell = "bell";
    public const string Mail = "mail";
    public const string Phone = "phone";
    public const string PhoneOff = "phone-off";
    public const string PhoneIncoming = "phone-incoming";
    public const string MessageSquare = "message-square";

    // Time & Calendar
    public const string Clock = "clock";
    public const string Timer = "timer";
    public const string CalendarCheck = "calendar-check";
    public const string CalendarX = "calendar-x";
    public const string CalendarPlus = "calendar-plus";

    // Files & Data
    public const string File = "file";
    public const string FileText = "file-text";
    public const string Folder = "folder";
    public const string Database = "database";
    public const string BarChart = "bar-chart-2";
    public const string PieChart = "pie-chart";

    // People & Teams
    public const string UserPlus = "user-plus";
    public const string UserMinus = "user-minus";
    public const string UserCheck = "user-check";
    public const string UserX = "user-x";
    public const string UsersRound = "users-round";
    public const string Contact = "contact";

    // Security & Auth
    public const string Lock = "lock";
    public const string Unlock = "unlock";
    public const string Shield = "shield";
    public const string Key = "key";
    public const string LogOut = "log-out";
    public const string LogIn = "log-in";

    // Misc
    public const string Star = "star";
    public const string Heart = "heart";
    public const string Flag = "flag";
    public const string Tag = "tag";
    public const string MapPin = "map-pin";
    public const string Globe = "globe";
    public const string Sun = "sun";
    public const string Moon = "moon";
    public const string Eye = "eye";
    public const string EyeOff = "eye-off";
    public const string Undo = "undo";
    public const string Redo = "redo";
}
