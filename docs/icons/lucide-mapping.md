# Lucide Icon Mapping

Authoritative emoji→Lucide name mapping for the ShiftManager migration.
Used by both humans (when editing `.cshtml` / `.js`) and the migration
ledger (`emoji-ledger.json`). All icons are rendered by `IconTagHelper.cs`
(server) or `icon-runtime.js` (client); both read from the same SVG path
dictionary.

## Conventions

- Use the **Lucide name** column as the `name=""` attribute value.
- When a preferred name is missing from `IconTagHelper.cs`, use the **Fallback** instead.
- `help-circle` is the universal "unknown" fallback — never throws.
- `size` defaults to `md` (20px). Tokens: `xs`(12), `sm`(16), `md`(20), `lg`(24), `xl`(32), `2xl`(48). Numeric pixel sizes also accepted.

## Mapping

| Emoji | Lucide | Fallback | Primary usage |
|---|---|---|---|
| 📅 | calendar | — | Calendar nav, date labels, My Shifty |
| 📋 | clipboard-list | file-text | Day view, lists, command palette |
| 🧹 | sparkles | star | Chores (cleanup/tidy metaphor) |
| ⚠️ | alert-triangle | — | Warnings, conflicts, understaffed |
| 📊 | bar-chart-2 | — | Analytics, reports |
| 👤 | user | — | Profile, user items |
| 📝 | pencil | edit | Requests, forms |
| 🔔 | bell | — | Notifications |
| 🎯 | target | check-circle | On-duty roster |
| ⚙️ | settings | — | Settings, configuration |
| ✅ / ✓ | check | — | Success, fully staffed |
| ❌ / ✗ | x | — | Error, needs attention |
| 💾 | save | — | Save, draft |
| ✏️ | pencil | — | Edit |
| 🗑️ | trash-2 | — | Delete |
| ⏰ | alarm-clock | clock | Time, shift times |
| 🏠 | home | — | Home/landing |
| 👥 | users | — | Groups, teams |
| 🏢 | building-2 | — | Companies |
| 📜 | scroll-text | file-text | Audit log |
| 🕐 | clock | — | Shift types |
| 🏖️ | palm-tree | globe | Time off, vacation |
| 🛡️ | shield | — | Backup-hakam duty type |
| 🎨 | palette | — | Theme picker self-reference |
| 🌍 | globe | — | Global config, locale |
| ☰ | menu | — | Mobile hamburger |
| ✉️ | mail | — | Messages |
| ⚡ | zap | — | Quick actions |
| 🔍 | search | — | Search |
| 🔒 | lock | — | Privacy, security |
| 🗂️ | folder | folder-tree | Organization tree |
| 📐 | ruler | help-circle | Blueprints |
| ⭐ | star | — | Leaderboard, favorites |
| 👮 | shield-check | shield | Officer roles |
| ➕ | plus | — | Add |
| ➖ | minus | — | Remove |
| 🔧 | settings | — | Config / wrench |
| 📦 | folder | package | Packaging, bundles |
| 🚀 | zap | — | Launch, ship |
| 💡 | help-circle | info | Hints, tips |
| 🔗 | link | — | External links |

## Scope exceptions — emojis intentionally kept

- **ShiftSwap game tiles** (`wwwroot/js/shift-swap-game.js`): 📅 🔔 ⏰ 🧹 ☕ 📦 — game *content*, not UI chrome. Monochrome icons can't deliver match-3 visual variety.

## Tag helper vs JS renderer

| Context | Use |
|---|---|
| `.cshtml` static markup | `<icon name="calendar" />` |
| `.cshtml` dynamic (inside a Razor `@if`, etc.) | `<icon name="calendar" />` (still works) |
| `.js` DOM construction | `Icons.render('calendar', { size: 20 })` returns `<svg>` string |

The JS renderer's dictionary is a subset of the server's; add any missing
icons to `wwwroot/js/icon-runtime.js` `ICONS` map when needed client-side.
