using ShiftManager.Data.SeedData;
using ShiftManager.Models.Navigation;

namespace ShiftManager.Services.Navigation;

/// <summary>
/// THE single source of truth for the domain-hub navigation tree (Model B redesign).
///
/// Every leaf's <see cref="NavNode.Policy"/> is the SAME authorization policy its
/// destination page enforces, so <see cref="NavigationService"/> can derive visibility
/// from <c>IAuthorizationService</c> (design principle P2 — visibility == access).
/// <c>NavRegistryPolicyParityTests</c> reflects over each route's PageModel and asserts the nav
/// policy is never looser than the page's [Authorize(Policy)] (so no visible-but-403 can drift in).
///
/// Phase 1 points leaves at EXISTING routes so nothing 404s and nothing is physically moved.
/// Later phases repoint individual leaves at merged/hosted surfaces (e.g. Eligibility home,
/// scope-aware Rules) behind the same FF_NEW_NAV flag.
/// </summary>
public static class NavRegistry
{
    // Grant policy-name helper (matches GrantPolicyProvider's "Grant:" prefix).
    private static string G(string grantKey) => "Grant:" + grantKey;

    private static NavNode Link(string loc, string route, string? policy = null, string? icon = null, string? flag = null, string? activeMatch = null)
        => new(loc, Route: route, Policy: policy, Icon: icon, Flag: flag, ActiveMatch: activeMatch);

    private static NavNode Hub(string loc, string icon, params NavNode[] children)
        => new(loc, Icon: icon, Children: children);

    private static NavNode SubGroup(string loc, params NavNode[] children)
        => new(loc, Children: children);

    /// <summary>The complete navigation tree (pre-authorization). Order = display order.</summary>
    public static readonly IReadOnlyList<NavNode> Root = new List<NavNode>
    {
        // ── Home ────────────────────────────────────────────────────────────────
        Link("Nav2_Home", "/Home/Index", icon: "home", activeMatch: "/Home"),

        // ── Scheduling (viewer label "My Schedule" via role-aware rendering) ──────
        Hub("Nav2_Scheduling", "calendar",
            SubGroup("Nav2_Sched_Calendars",
                Link("Schedule", "/Calendar/Shifts", icon: "calendar", activeMatch: "/Calendar/Shifts"),
                Link("Chores", "/Calendar/Chores", policy: G("ViewChores"), icon: "brush", activeMatch: "/Calendar/Chores"),
                Link("OnDuty", "/Calendar/OnCall", policy: G("ViewDuties"), icon: "pin", activeMatch: "/Calendar/OnCall"),
                // #9: the old "Scheduled Shifts" (/Calendar/Table, manager-only roster grid) nav
                // leaf is REPLACED by the everyone-visible Team page. /Calendar/Table still works by
                // URL; it just loses its sidebar shortcut (approved). null policy = any authenticated
                // user (Team page is [Authorize] with no policy — nav parity requires the match).
                Link("Nav_Team", "/Calendar/Team", icon: "users", activeMatch: "/Calendar/Team"),
                // #9: renamed from "Company Overview" — this whole-company view IS the user's "desk".
                Link("MyDesk", "/Calendar/Overview", icon: "eye")),
            SubGroup("Nav2_Sched_Planning",
                Link("Nav2_ShiftPlans", "/Owner/Programs", policy: G("ManagerHomeAccess"), icon: "clipboard-list", activeMatch: "/Owner/Programs"),
                Link("Nav2_MasterPlans", "/Owner/MasterPrograms", policy: G("ManagerHomeAccess"), icon: "layers-3"),
                Link("Nav2_ChoreTemplates", "/Admin/Organization/ChoreTemplates", policy: G("EditChoreTypes"), icon: "stamp"),
                Link("DutyRotations", "/Admin/DutyRotation", policy: G("ManageOnDuty"), icon: "refresh-cw", flag: FeatureFlagSeed.Flags.DutyRotationEnabled)),
            SubGroup("Nav2_Sched_Definitions",
                Link("Nav2_Blueprints", "/Owner/Blueprints", policy: G("ManagerHomeAccess"), icon: "layers"),
                Link("Nav2_ShiftGroupings", "/Admin/Organization/ShiftGroupings", policy: G("ManageShiftGroupings"), icon: "group"),
                // Chore-type defs belong with shift Blueprints / DutyTypes (parity spine), not the
                // Organization route they happen to be filed under — same call DutyTypes made above.
                Link("Nav2_ChoreTypes", "/Admin/Organization/ChoreTypes", policy: G("EditChoreTypes"), icon: "list-checks"),
                Link("Nav2_DutyTypes", "/Admin/Organization/DutyTypes", policy: G("ManageOnDutyTypes"), icon: "shield")),
            Link("Nav2_Eligibility", "/Scheduling/Eligibility", policy: G("ManageShiftCategories"), icon: "user-check"),
            Link("Nav2_Rules", "/Admin/Settings", policy: G("ViewSettings"), icon: "sliders-horizontal", activeMatch: "/Admin/Settings")),

        // ── Requests (own) + Approvals (managers) ────────────────────────────────
        Link("Requests", "/My/Requests", icon: "file-text", activeMatch: "/My/Requests"),
        Link("Nav2_Approvals", "/Requests/Index", policy: G("ManagerHomeAccess"), icon: "check-square", activeMatch: "/Requests"),

        // ── Oversight (Director) — refined to header lenses in Phase 6 ────────────
        Hub("Section_DirectorTools", "telescope",
            Link("Nav2_CrossCompanyApprovals", "/Director/NotificationHub", policy: G("DirectorHubAccess"), icon: "inbox"),
            Link("Section_ViewAsManager", "/Director/ViewAsMode", policy: G("DirectorHubAccess"), icon: "eye-off"),
            Link("CompanyFilter", "/Director/CompanyFilter", policy: G("DirectorHubAccess"), icon: "filter")),

        // ── People ───────────────────────────────────────────────────────────────
        Hub("Nav2_People", "users",
            Link("People", "/Admin/Users", policy: G("ManagerHomeAccess"), icon: "users", activeMatch: "/Admin/Users"),
            Link("JoinRequests", "/Admin/Users?view=joinrequests", policy: G("ManagerHomeAccess"), icon: "user-plus"),
            Link("Companies", "/Admin/Companies", policy: G("EditCompany"), icon: "building-2"),
            Link("Announcements", "/Admin/Announcements", policy: G("ManageAnnouncements"), icon: "megaphone"),
            Link("Nav2_HomeTypes", "/Admin/HomeTypes", policy: G("ManageHomeTypes"), icon: "house")),

        // ── Organization (structure) ──────────────────────────────────────────────
        Hub("Organization", "git-branch",
            Link("Nav2_Hierarchy", "/Admin/Organization", policy: G("ViewHierarchy"), icon: "git-branch", activeMatch: "/Admin/Organization"),
            Link("Nav2_JobTypes", "/Admin/Organization/JobTypes", policy: G("ManageJobTypes"), icon: "briefcase"),
            Link("Nav2_Stores", "/Admin/Organization/Stores", policy: G("ManageStores"), icon: "store", flag: FeatureFlagSeed.Flags.StoreHoursEnabled),
            Link("Nav2_AreaPalette", "/Admin/Organization/AreaPalette", policy: G("EditAreaCalendarPalette"), icon: "palette")),

        // ── Access (permissions) ──────────────────────────────────────────────────
        Hub("Nav2_Access", "key-round",
            Link("Nav2_Roles", "/Admin/Organization/Roles", policy: G("AssignRoles"), icon: "user-cog"),
            Link("Nav2_Grants", "/Admin/Organization/Grants", policy: G("ViewGrants"), icon: "key"),
            Link("Nav2_RoleTemplates", "/Owner/Hub/RoleTemplates", policy: G("AdminAccess"), icon: "copy"),
            Link("Nav2_Simulator", "/Owner/Hub/PermissionSimulator", policy: G("AdminAccess"), icon: "flask-conical"),
            Link("Nav2_Directors", "/Admin/Directors", policy: G("AssignRoles"), icon: "user-star"),
            Link("Nav2_LockedUsers", "/Owner/LockedUsers", policy: G("AdminAccess"), icon: "lock")),

        // ── Insights (observation) ────────────────────────────────────────────────
        Hub("Nav2_Insights", "line-chart",
            Link("SystemHealth", "/Owner/SystemHealth", policy: G("AdminAccess"), icon: "heart-pulse"),
            Link("Telemetry", "/Owner/Telemetry", policy: G("SystemConfiguration"), icon: "activity"),
            Link("AuditLog", "/Admin/AuditLog", policy: G("ManagerHomeAccess"), icon: "search", activeMatch: "/Admin/AuditLog"),
            Link("Analytics", "/Admin/Analytics", policy: G("ViewJusticeTable"), icon: "bar-chart-3")),

        // ── System (mutation) ─────────────────────────────────────────────────────
        Hub("Nav2_System", "server-cog",
            Link("Nav_FeatureFlags", "/Owner/FeatureFlags", policy: G("SystemConfiguration"), icon: "toggle-left"),
            SubGroup("Nav2_Integrations",
                Link("Nav2_EmailConfig", "/Owner/EmailConfig", policy: G("ConfigureEmailSettings"), icon: "mail"),
                Link("Nav2_EmailTemplates", "/Owner/EmailTemplates", policy: G("AdminAccess"), icon: "mail-plus"),
                Link("Nav2_Sso", "/Owner/GriffinConfig", policy: G("AdminAccess"), icon: "shield-check"),
                Link("Nav2_GameConfig", "/Owner/GameConfig", policy: G("AdminAccess"), icon: "gamepad-2")),
            Link("Nav2_Localization", "/Owner/LanguageManagement", policy: G("AdminAccess"), icon: "languages"),
            SubGroup("Nav2_ContentConfig",
                Link("Nav2_AreaConfig", "/Owner/AreaConfig", policy: G("AdminAccess"), icon: "map"),
                Link("Nav2_SetupTasks", "/Admin/SetupTasks", policy: G("SystemConfiguration"), icon: "list-todo", flag: FeatureFlagSeed.Flags.SetupTasksEnabled)),
            SubGroup("Nav2_DataOps",   // ⚠ danger zone — visually segregated in the renderer
                Link("Nav2_Backup", "/Owner/Backup", policy: G("AdminAccess"), icon: "database-backup"),
                Link("Nav2_DataLifecycle", "/Owner/DataLifecycle", policy: G("SystemConfiguration"), icon: "archive"),
                Link("Nav2_ExportUserData", "/Owner/Hub/ExportUserData", policy: G("AdminAccess"), icon: "file-down"),
                Link("Nav2_DatabaseConsole", "/Owner/DatabaseConsole", policy: G("SystemConfiguration"), icon: "terminal"))),

        // ── Me (everyone) ─────────────────────────────────────────────────────────
        Hub("Nav_Personal", "user",
            Link("MyProfile", "/My/Profile", icon: "user"),
            Link("My_Settings", "/My/Settings", icon: "settings"),
            Link("MyElig_NavLink", "/My/Eligibility", icon: "badge-check"),
            Link("MyGroups", "/MyTeam/Index", icon: "users"),
            Link("MyFriends", "/Friends", icon: "heart-handshake", flag: FeatureFlagSeed.Flags.FriendshipsEnabled),
            Link("Help_NavLink", "/My/Help", icon: "help-circle")),
    };

    /// <summary>Flat list of every leaf (route-bearing) node — used by the palette + parity test.</summary>
    public static IEnumerable<NavNode> AllLeaves()
    {
        IEnumerable<NavNode> Walk(IEnumerable<NavNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Route is not null) yield return n;
                foreach (var c in Walk(n.ChildNodes)) yield return c;
            }
        }
        return Walk(Root);
    }
}
