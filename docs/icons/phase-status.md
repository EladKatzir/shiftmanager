# Lucide + Theme Migration — Phase Status

_Last updated: 2026-04-16 — build green, lint green, migration applied._

Two-feature migration: emoji → Lucide + hidden Shift+Click theme picker.

## Build & test snapshot

| Check | Status |
|---|---|
| `dotnet build --nologo` | ✅ 0 warnings, 0 errors |
| `dotnet ef database update` | ✅ Applied `20260415084229_ThemePreferences` |
| `dotnet test --filter ResxEmojiLint` | ✅ 2 passed (EN + he-IL `.resx` both clean) |
| `dotnet test --filter IconDictDrift` | ✅ 1 passed (client icon dict ⊆ server icon dict) |
| SQLite verification of `Users.ThemeColor/ThemeMode` | ✅ columns present |

## Phase 1 — Infrastructure ✅ DONE

| Artifact | Status | Path |
|---|---|---|
| Icon tag helper (base 60 icons) | ✅ | `TagHelpers/IconTagHelper.cs` |
| Icon tag helper extensions (+25) | ✅ | sparkles, target, clipboard-list, scroll-text, alarm-clock, palm-tree, folder-tree, bar-chart-3, zap, palette, shield-check, inbox, keyboard, radio, megaphone, pin, medal, sliders, book-open, store, landmark, hammer, trending-up, party-popper, siren, construction |
| Icon JS runtime | ✅ | `wwwroot/js/icon-runtime.js` (extended with target/palette/zap/radio/etc.) |
| Mapping doc | ✅ | `docs/icons/lucide-mapping.md` |
| Emoji ledger | ✅ | `docs/icons/emoji-ledger.json` |
| Resx lint test | ✅ **ACTIVE** | `ShiftManager.Tests/Migration/ResxEmojiLintTests.cs` |
| **Icon-dict drift test** | ✅ **ACTIVE** | `ShiftManager.Tests/Migration/IconDictDriftTests.cs` — fails CI if `icon-runtime.js` adds a key not in `IconTagHelper.cs` |

## Phase 2 — Additive migration 🟢 DONE (main sweep)

| Batch | Files | Replacements |
|---|---|---|
| `_Layout.cshtml` sidebar + chrome | 1 | 5 |
| `site.js` command palette + renderer | 1 | 20 items + renderer swap |
| Admin pages (Phase 2A agent) | 9 | 71 |
| Owner pages (Phase 2B agent) | 14 | 224 |
| Calendar + My + Auth + Home + Public (Phase 2C agent) | 20 | ~95 |
| Calendar helper JS + Hierarchy (Phase 2D agent) | 4 | 14 |
| `DutyTypes/Index` placeholder (Lucide-aware) | 1 | 1 |
| **Grand total** | **~50 files** | **~430** |

## Phase 3 — Reductive migration 🟢 DONE

| Step | Status |
|---|---|
| Strip `.resx` leading/trailing emojis (Phase 3 agent) | ✅ 70 entries across both `.resx` files |
| Strip residual `🎌 ⚠ ⏳` (not in initial strip list) | ✅ Done manually |
| Enable `ResxEmojiLintTests` | ✅ Passes 2/2 |
| Narrow lint regex to exclude plain arrows (U+2190-21FF) | ✅ Legit typography `→` in ADFS instructions preserved |
| `OnDutyTypeConfig.Icon` semantic migration | ✅ Seed `"🛡️"` → `"shield"`; default `"📌"` → `"pin"`; `Config.cshtml` + `DutyTypes/Index.cshtml` renderers detect ASCII-name vs legacy emoji |
| C# string literal strip (UI chrome files) — Phase 3b agent | ✅ 5 strips in `GriffinConfig.cshtml.cs` + `EmailConfig.cshtml.cs`; 51 sites flagged as `.Icon` DTO render pattern (handled by Phase 3c agent) |
| Coordinated `.Icon` render refactor (Phase 3c agent) | 🟡 In progress — swapping emoji→Lucide in CalendarItem/activity/scope/DutyType DTOs + adding conditional render to matching views |

### Phase 3 — explicit leaves (not stripped)

- `Pages/GriffinDiagnostic.cshtml.cs` (43 emoji literals) — debug page; human review
- `Services/MailService.cs` (9) — email *content*, not UI chrome
- `Pages/My/NotificationCenter.cshtml.cs` (7) — notification content; may be data
- `Pages/Auth/Login.cshtml.cs` (1), `Program.cs` (1) — one-offs, human review
- All `_logger.Log*(...)` calls containing emoji — logs are operational, not chrome
- All `throw new Exception("...emoji...")` — stable error text for monitoring

## Phase 4 — Theme picker ✅ DONE

| Artifact | Status |
|---|---|
| `AppUser.ThemeColor` / `.ThemeMode` | ✅ in DB (migration applied) |
| EF migration `20260415084229_ThemePreferences` | ✅ |
| GET/POST/DELETE `/Api/My/Theme` | ✅ |
| Middleware bypass for `/Api/My` | ✅ |
| Theme engine (HSL derivation, FOUC-free) | ✅ |
| Theme picker modal | ✅ Canvas wheel + 8 presets + light/dark/auto + live preview + toasts |
| `_Layout.cshtml` theme integration | ✅ `theme-engine.js` loaded before any stylesheet |
| Shift+Click branch in `site.js` | ✅ Lazy-loads picker assets on first open |
| Login-time cookie seeding | ✅ `Pages/Auth/Login.cshtml.cs` |

## Phase 5 — Polish + QA ⏸ DEFERRED

Requires a running instance + Playwright:

- [ ] Playwright visual regression (20 pages × light/dark × EN/he-IL = 80 shots)
- [ ] WCAG contrast sweep across 50 random hues
- [ ] Hebrew RTL verification on icon-heavy pages
- [ ] ShiftSwap regression — Ctrl+Click still triggers game
- [ ] Cross-browser smoke — Edge, Chrome, Firefox
- [ ] `/Api/My/Theme` integration test — user A posting must not mutate user B

## Explicit rejections (per plan)

- ❌ Per-company theming
- ❌ Full palette override
- ❌ Dual-color picker
- ❌ Semantic / domain color customization
- ❌ Font / spacing / radius customization
- ❌ Theme import/export
- ❌ ShiftSwap tile icons (documented)

## Rollback safety

- `AppUser.ThemeColor = NULL` + `ThemeMode = NULL` disables all overrides (no JS crash, no CSS effect).
- Icon tag helper with unknown `name` renders `help-circle` + `console.warn` — never throws.
- `OnDutyTypeConfig.Icon` + DTO `.Icon` renderers detect ASCII-vs-emoji → legacy data renders unchanged.
- `theme-engine.js` removal → app falls back to `tokens.css` defaults.
