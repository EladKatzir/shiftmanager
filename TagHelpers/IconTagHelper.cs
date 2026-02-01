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
        ["printer"] = "<path d=\"M6 9V2h12v7M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2M6 14h12v8H6z\"/>",
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
        ["building"] = "<path d=\"M6 22V4a2 2 0 012-2h8a2 2 0 012 2v18Z\"/><path d=\"M6 12H4a2 2 0 00-2 2v6a2 2 0 002 2h2\"/><path d=\"M18 9h2a2 2 0 012 2v9a2 2 0 01-2 2h-2\"/><path d=\"M10 6h4\"/><path d=\"M10 10h4\"/><path d=\"M10 14h4\"/><path d=\"M10 18h4\"/>",
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
        ["redo"] = "<path d=\"M21 7v6h-6\"/><path d=\"M3 17a9 9 0 019-9 9 9 0 016 2.3L21 13\"/>"
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
