# ShiftManager UI Overhaul - Design Decisions Summary

## Project Overview
Complete UI overhaul of ShiftManager ("Shifty"), a military shift scheduling system, transforming it into a warm, human, distinctively branded workforce management experience.

## Plan File Location
`C:\Users\katzi\.claude\plans\polymorphic-exploring-flamingo.md` - Version 3.0 (Final)

---

## Key Design Decisions

### Theme & Brand
| Decision | Choice |
|----------|--------|
| Theme Direction | "Warm Command" — Professional command center with human warmth |
| Brand Mark | Custom "SHIFTY" wordmark with swoosh (from user sketch shiftysymbol.png) |
| Logo Elements | Custom rounded letterforms + flowing swoosh + deep navy blue + glossy depth |
| Brand Personality | Warm, Clear, Reliable, Distinctive, Military-Professional |

### Color System
| Token | Light Mode | Dark Mode |
|-------|------------|-----------|
| Primary | #1E3A5F (Deep Navy) | #5B9BD5 (Sky Blue) |
| Accent | #5B9BD5 (Sky Blue) | #87CEEB (Brighter sky) |
| Background | #F8F9FB | #0F1419 |
| Surface | #FFFFFF | #1A2332 |

**Shift Type Colors (Time-of-day metaphor):**
- Morning: #F0C14B (Amber) - Sunrise warmth
- Afternoon: #6B7F59 (Olive) - Midday calm
- Night: #1E3A5F (Navy) - Evening depth
- Hakam: #8B4513 (Saddle Brown) - Distinguished
- BR: #4A5568 (Slate) - Neutral authority

### Navigation Architecture
| Component | Decision |
|-----------|----------|
| Navigation Model | Collapsible categories with grant-based item population |
| Context Switcher | Searchable dropdown in sidebar, remembers last used |
| Scope Switcher | Separate control above calendars (Mine/Company/Molecule) |
| Persistence | localStorage for user preferences |

**Key Distinction:** Context (which company/molecule) and Scope (how much to show) are SEPARATE controls.

### Authorization Model
- **GRANTS determine everything** (NOT roles)
- Roles just bundle grants together
- UI adapts additively based on grants
- If user has grants for Company A + Company B → sees UNION of both (additive merging)

### ShiftGroupings Understanding
- A user in Hir sees ENTIRE molecule's shifts for their JobType
- Shifts are LABELED by ShiftGrouping name (Darom, Tzafon, Tacti)
- Eligibility is controlled by grants, not groupings

### Widget System
| Decision | Choice |
|----------|--------|
| Location | Sidebar with option to collapse |
| Customization | Per user (choose what numbers, can add friends) |
| Primary Widget | On-Call Contact Card with phone numbers |
| Grant Merging | Additive - sees union of all granted content |

### Design Agents (7 Total)
1. Brand Designer - Logo, colors, typography
2. UX Researcher - User flows, pain points
3. UI Designer - Layouts, components, visual polish
4. Interaction Designer - States, transitions, animations
5. Accessibility Specialist - WCAG AA, contrast, keyboard, ARIA
6. Design Systems Lead - Tokens, specs, documentation
7. **Localization Specialist** - Runs after EVERY feature (critical)

### Accessibility
- Target: WCAG AA compliance
- Contrast: 4.5:1 minimum for text
- All text uses `<loc key="...">` tags
- ARIA labels must be localized
- RTL support for Hebrew (he-IL)

### Typography
- Font Stack: 'Inter', system-ui, -apple-system, 'Segoe UI', sans-serif
- Hebrew: 'Heebo', 'Rubik', sans-serif
- Mono: 'JetBrains Mono', 'Fira Code', monospace

### Component Patterns Defined
- Buttons (states: default, hover, active, disabled, loading)
- Inputs (states: default, focus, error, disabled, readonly)
- Modals (sizes: small 400px, medium 560px, large 800px, full 90vw)
- Tables (sortable, selectable, paginated)
- Forms (validation timing, button order LTR/RTL)
- Empty states (context-specific, actionable)
- Loading states (skeleton preferred, 300ms delay for spinner)
- Error states (toast, banner, inline validation)
- Print styles (B&W friendly, hide chrome, page breaks)

### Date/Time Formats
- Default: 24-hour format (HH:mm)
- Full date: "January 30, 2026" / "30 בינואר 2026"
- Relative time: "X minutes ago" / "לפני X דקות"
- Hebrew dates: Right-to-left, different order

---

## Implementation Phases
1. Design tokens + core components
2. Navigation shell + scope switcher
3. Calendar views redesign
4. Widget system (grant-based)
5. All remaining pages
6. Polish, RTL, accessibility audit

## Critical Files to Modify
- `wwwroot/css/site.css` → Replace with modular token system
- `wwwroot/css/tokens.css` → NEW: Design tokens
- `wwwroot/css/components.css` → NEW: Component styles
- `Pages/Shared/_Layout.cshtml` → New navigation structure
- `Pages/Shared/Components/` → New Razor components
- `Pages/Calendar/*.cshtml` → Scope switcher integration
- `Services/WidgetService.cs` → NEW: Grant-based widget logic

## Grant-Based UI Visibility Pattern (A-020)

### Implementation Pattern
The system uses grant-based authorization for UI visibility:

1. **UI Layer** (`.cshtml` files): Use `<require-grant>` tag helper
   ```html
   <require-grant key="ViewAnalytics">
     <a href="/Admin/Analytics">Analytics</a>
   </require-grant>
   
   <!-- Multiple grants with any/all mode -->
   <require-grant key="AssignChores,AssignHakamDuties" mode="any">
     <button>Quick Add</button>
   </require-grant>
   ```

2. **Server Layer** (`.cshtml.cs` PageModels): Use `[Authorize(Policy = "Grant:*")]`
   ```csharp
   [Authorize(Policy = "Grant:ViewAnalytics")]
   public class AnalyticsModel : PageModel { }
   ```

3. **Data Layer** (ViewComponents/Services): Inject `IGrantService`
   ```csharp
   var hasGrant = await _grantService.HasGrantAsync(userId, "ViewShifts");
   ```

### Grant Key Reference (Common)
- `ViewShifts`, `ViewChores`, `ViewDuties` - Calendar view permissions
- `AssignAlhutShifts`, `AssignTextShifts`, `AssignBRShifts` - Shift assignment
- `AssignChores`, `AssignHakamDuties`, `AssignKatzinDuties` - Assignment permissions
- `ViewAnalytics`, `ViewAuditLog`, `ViewSettings` - Admin access
- `AdminAccess` - System-wide admin (Owner panel)
- `EditCompany`, `EditMolecule`, `EditArea` - Hierarchy management

### Organizational Context (Exception)
The Context Switcher intentionally uses role-based checks because it reflects
organizational membership (Owner=all, Director=assigned companies, User=own company),
not functional permissions. This is documented in the code.

## What We Will NOT Do
- ❌ Dark-only theme (keep light as default)
- ❌ Animated backgrounds or distracting motion
- ❌ Hide features users have grants for
- ❌ Break existing URL structures
- ❌ Change calendar grid fundamentally
- ❌ Add features not requested (YAGNI)

## Game Preservation
- Shift Swap Game (Easter egg Match-3 puzzle) must be preserved
- Entry point: 10-tap sequence on logo
- Has its own API endpoints for localization

## Localization Requirements
- All visible text: `<loc key="...">` tags
- Languages: English (en-US) + Hebrew (he-IL with RTL)
- Resource files: Resources/SharedResources.resx, SharedResources.he-IL.resx
- Key naming: Page_Component_Description pattern
- Localization Specialist agent runs after EVERY feature completion
