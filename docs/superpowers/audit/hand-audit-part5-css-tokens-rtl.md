# Hand audit part 5 — CSS custom properties, contrast, and RTL

**Date:** 2026-08-04 · main-loop mechanical sweep + hand verification
(`scratchpad/rtl_contrast.py`). This dimension had never been audited.

---

## X-C01 (MEDIUM) — 15 CSS custom properties are used but never defined

`var(--x)` where `--x` is defined nowhere resolves to `unset`, making the declaration **invalid at
computed-value time** — the property silently falls back to inherited or initial. The declaration
looks correct in review and does nothing at runtime.

**Root cause: a naming-convention mismatch.** The design system defines
`--space-0…24`, `--space-2xl/3xl`, `--radius-none/sm/md/lg/xl/2xl/full`, `--primary`, `--text`.
The code below references names from a *different* convention that was never adopted:

| Used (undefined) | The token that actually exists | Example site |
|---|---|---|
| `--spacing-xs` / `-sm` / `-md` / `-lg` / `-xl` | `--space-1` … `--space-24` | `wwwroot/css/site.css:6009,6060,6075,6363` |
| `--radius` | `--radius-md` (or `-sm`/`-lg`) | `wwwroot/css/distribution-lists.css:43` |
| `--primary-color`, `--primary-color-light` | `--primary`, `--primary-soft` | `wwwroot/css/site.css:6068` |
| `--text-primary` | `--text` | `wwwroot/css/site.css:7356` |
| `--text-xs` / `-sm` / `-lg` | the font-size scale | `wwwroot/css/components.css:2386,2397,3766` |
| `--muted-rgb`, `--border-rgb` | (no rgb-triplet tokens exist) | `wwwroot/css/site.css:1377,1427` |
| `--cal-toolbar-height` | (never defined) | `wwwroot/css/calendar.css:3477` |

`--muted-rgb` / `--border-rgb` are the most likely to be visibly wrong: they appear inside
`rgba(var(--muted-rgb), 0.5)`-style expressions, and an undefined triplet makes the **entire colour
function invalid**, so the element gets no background/border at all rather than a faded one.

**Excluded as false positives** (verified set inline from markup, so correctly undefined in CSS):
`--group-color` (`Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`), `--cell-index`
(`_MonthSkeleton.cshtml`), `--dash-total` / `--dash-offset` (`Pages/Admin/Analytics.cshtml`),
`--_role-color`.

**Proposed fix:** replace each with its real token. **Durable fix:** a build/test check that every
`var(--x)` in `wwwroot/css` resolves to a defined property (excluding an allow-list of
inline-set ones) — the sweep script already does this.

---

## REFUTED — `.shift-table th` is NOT a contrast defect

My sweep flagged `wwwroot/css/calendar.css:1128` under the project's documented "blanket `!important`
colour on base table elements" anti-pattern. Reading it disproves that:

```css
.shift-table th {
  background-color: var(--primary);
  color: var(--primary-contrast) !important;
```

That is **exactly the pattern the rule prescribes** — a dark background paired with an explicit
`--primary-contrast` colour marked `!important` so inherited/blanket colour cannot override it. The
documented anti-pattern is a blanket `color: … !important` on bare `table/th/td/tr` selectors that
defeats component white-on-dark; this is the component itself doing the right thing.

Remaining `!important`-on-table hits are all in `wwwroot/css/print.css` (print-only, lower risk) and
were not individually assessed.

---

## Method limitations — what I did NOT conclude, and why

Two of the three checks produced output too noisy to report as findings. Stating this rather than
padding the report:

### Contrast (`[A]`) — 310 raw hits, not reportable
The detector matched `background: var(--primary…)`, which also matches **`--primary-soft`**,
**`--primary-hover`** and similar *light tints* that are correctly paired with dark text. A large but
unmeasured fraction of the 310 are therefore correct-by-design. Distinguishing them needs either a
token-luminance table or actual rendering. **No contrast defect is claimed.**

### RTL (`[C]`) — 225 raw hits, not reportable
Physical `left`/`right`/`margin-left`/`padding-right`/`text-align` declarations outside a
`[dir]`-scoped rule, concentrated in `site.css` (77), `calendar.css` (36), `components.css` (31).
Many are legitimate — a centred absolute element using `left: 50%; transform: translateX(-50%)` is
direction-neutral, and some rules are genuinely LTR-only (code blocks, numeric columns). Separating
real breakage from benign use requires rendering the pages in Hebrew, which was not done.

### What this means for the RTL dimension
**RTL layout remains genuinely unaudited.** The right next step is not more static analysis — it is
loading the main pages at `PreferredLanguage = 'he-IL'` and looking at them. Note the harness trap
recorded during this audit: the `.AspNetCore.Culture` cookie has **no effect for logged-in users**
(`Users.PreferredLanguage` wins), so a cookie-based "Hebrew pass" silently tests English.

The JS positional check (`scrollLeft` / `offsetLeft` arithmetic with no RTL branch) produced no
output in the run captured here and should be re-run before relying on that negative.
