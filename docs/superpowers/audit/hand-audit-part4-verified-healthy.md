# Hand audit part 4 — verified-healthy surfaces

**Date:** 2026-08-04 · main-loop verification (no subagents).

Negative results matter as much as findings: they bound the risk and stop the next person
re-auditing ground that is already solid. Everything below was checked by reading the code, not
inferred.

---

## Auth contracts — all three clean

The codebase has three documented contracts on the authentication surface. A past defect
(GRIFFIN-USERLOOKUP-510) came from violating the first, producing "ghost accounts" that existed but
could never authenticate. All three now hold everywhere:

### 1. Case-insensitive email lookup — 18/18 sites canonical

The contract is: pre-lower the input in C#, then compare against `column.ToLower()` (which EF
translates to SQL `LOWER()`), never `ToLowerInvariant()` inside an `IQueryable` (untranslatable →
runtime `InvalidOperationException`).

Every site follows it:
`Pages/Auth/Login.cshtml.cs:226`, `Pages/Auth/Signup.cshtml.cs:253,265`,
`Pages/Auth/GriffinSignup.cshtml.cs:288,304`, `Pages/Auth/ForgotPassword.cshtml.cs:125-129,267-270`,
`Pages/Admin/Users.cshtml.cs:866-867,2533-2534,2868-2869`.

`ForgotPassword.cshtml.cs:123` even carries the explanatory comment
(*"EF Core translates u.Email.ToLower() → SQL LOWER(); ToLowerInvariant would throw"*). **No
deviation found — the ghost-account bug class is closed.**

### 2. Password hashing — no raw primitives anywhere

Every hash/verify goes through `PasswordHasher.CreateHash` / `PasswordHasher.Verify` (PBKDF2 /
SHA256 / 100k iterations): `Auth/Login:257`, `Auth/Signup:287`, `Auth/ForgotPassword:152,281,289`,
`Admin/Users:985,1994,3305`, `Admin/Companies:329`, `Services/Api/UserApiService:161,168`.

A repo-wide grep for `HMACSHA`, `new SHA256(`, or `Rfc2898` outside the helper returns **nothing** in
production code. (A past bug created API users with raw HMACSHA512; that is gone.)

### 3. Claim parsing — no `int.Parse` on claims

The contract is `int.TryParse` always, because a malformed claim would otherwise throw. A repo-wide
grep for `int.Parse(...Claim...)` / `int.Parse(...FindFirst...)` across `Pages/` and `Services/`
returns **zero** hits.

### Bonus control worth keeping
`Pages/Owner/SystemHealth.cshtml.cs:288` verifies whether the owner account still has the default
password `admin123` and surfaces a warning. That is a genuinely useful default-credential check.

---

## `/Owner/DatabaseConsole` — the documented hardening is real

This surface previously had a SQL-injection bypass via multi-statement queries. The fix is layered
and, unusually for this audit, the code matches its comments:

1. `[Authorize(Policy = "Grant:SystemConfiguration")]` (`:15`).
2. `:63` — the query must `StartsWith("SELECT")` after uppercasing.
3. `:69-72` — **semicolons are rejected outright**, so no second statement is reachable.
4. `:84-87` — execution uses a dedicated connection with **`Mode = SqliteOpenMode.ReadOnly`**.

Point 4 is the load-bearing control: even if a string check were bypassed, SQLite itself refuses
writes. Points 2-3 are belt-and-braces.

**Bypasses probed and rejected:**
- A trailing `--` cannot escape the row-cap wrapper at `:97`
  (`SELECT * FROM ({query}) LIMIT n`) — commenting out the tail leaves an unclosed parenthesis, so it
  fails closed with a syntax error.
- `PRAGMA ...` and `WITH ... SELECT` do not pass `StartsWith("SELECT")` (the latter is a usability
  false-negative, not a hole).
- Multi-statement payloads are blocked by the semicolon rule before reaching SQLite.

**Residual, by design:** the console grants read access to all data to holders of
`SystemConfiguration`. That is its purpose, not a defect.

**Minor, non-blocking:** `:61` uses culture-sensitive `ToUpper()` rather than
`ToUpperInvariant()`. For the ASCII literal `"SELECT"` this is harmless in practice; using the
invariant overload would be more correct.

---

## Why this file changes the reading of the whole audit

The Database Console is the **first place in this audit where a `SECURITY` comment matched the
implementation**. Compare it with the eight controls that are provisioned and never wired, and with
`Delete.cshtml.cs:18` / `RoleService.AssignRoleAsync`, whose comments assert guarantees the code does
not provide.

The conclusion is not that this team cannot build layered defenses — this file proves they can and do.
It is that the codebase has **drifted**: grants were collapsed, eligibility was replaced, approval was
rewritten, and the *claims* surrounding those mechanisms were never revisited. Comments and seed data
kept asserting the old guarantees after the enforcement moved or vanished.

That reframing matters for the fix: the durable remedy is not "be more careful", it is the two guard
tests recommended in the master summary, which make this drift **detectable** rather than dependent on
anyone remembering.
