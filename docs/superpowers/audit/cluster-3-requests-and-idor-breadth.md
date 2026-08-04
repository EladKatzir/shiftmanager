# Breadth pass — app-wide IDOR, multi-company scope, and Cluster 3 (Requests/Approvals)

**Date:** 2026-08-03. **Status: PARTIAL — bounds below.**

## Scope bound

A 5-dimension flat parallel pass; **3 of 5 returned** (app-wide IDOR, multi-company scope,
Requests/Approvals). **2 died on the session token limit and produced nothing:**
`c2-role-templates` (RoleTemplate coherence + auto-grant lifecycle + seed id-vs-comment integrity)
and `c1-chores` (**`/Calendar/Chores` has now failed to be audited FOUR times and remains
completely unaudited**).

25 findings: **4 critical, 12 high, 8 medium, 1 low.** Each carries the auditor's own
`self_refutation_attempt` (what guard they looked for and why it does not save the code).
**Only the findings in the MAIN-LOOP VERIFIED section below were independently confirmed by
hand** — the rest are single-agent output and may contain false positives.

Each auditor also reported what it could NOT reach; notably the IDOR sweep never opened
`Pages/Owner/Backup.cshtml.cs` (RestoreBackup/DeleteBackup take client-supplied ids), and the
Requests auditor never opened `Pages/Requests/Swaps/*`.

## MAIN-LOOP VERIFIED

### B-F01 (CRITICAL) — any authenticated user can steal a colleague's API key

**Verified end to end by hand.** Chain:

1. `Pages/My/ApiKeys.cshtml.cs:13` — the page is bare `[Authorize]`: **every authenticated user**,
   including a Trainee, can reach it.
2. `Pages/My/ApiKeys.cshtml.cs:152` `OnPostRefreshAsync(int keyId)` forwards the client-supplied
   `keyId` verbatim to the service. Its only other input is the CALLER's own id.
3. `Services/ApiKeyService.cs` `RegenerateApiKeyAsync(int keyId, int regeneratedBy)`:
   ```csharp
   var companyId = _tenantResolver.GetCurrentTenantId();
   var apiKey = await _context.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.CompanyId == companyId);
   ```
   The ONLY predicate is the caller's **company** — there is **no ownership check**.
   `regeneratedBy` is used only in the log line, never for authorization.
4. The method rotates the victim's key (`apiKey.KeyHash = keyHash`) and RETURNS the new plaintext.
5. `Pages/My/ApiKeys.cshtml:37` renders `@Model.GeneratedApiKey` in plaintext, with a
   copy-to-clipboard button at `:39`.

**Impact:** a Trainee POSTs `handler=Refresh&keyId=<any key id in their company>` and receives a
working API key belonging to a colleague, carrying whatever `approvedScopes` and
`rateLimitPerMinute` an admin granted that key (set in `OnPostApproveAsync`). Simultaneously the
victim's existing key stops working — so this is credential theft AND denial of service in one
request. No crafted tooling needed beyond changing one integer.

**Mitigation present:** the action is audit-logged as `ApiKey.Regenerated` with the acting user, so
it is detectable after the fact — but nothing prevents it.

**Proposed fix:** add `&& k.UserId == regeneratedBy` (or the owning-user equivalent) to the
predicate, and rename the parameter to make its authorization role explicit. Apply the same check
to every other handler on this page that takes a `keyId` from the client.

### B-F02 (CRITICAL) — the vacation approval state machine is dead code

**Verified by hand.** A repo-wide grep for callers (excluding tests and publish copies):
`VacationApprovalService.ApproveAsync` -> **0 production callers**.
`VacationApprovalService.DeclineAsync` -> **0 production callers**.
(`CancelRequestAsync` DOES have callers: `Pages/Api/TimeOffRequest.cshtml.cs:82`,
`Pages/My/Requests.cshtml.cs:457` — so the service is wired in general, which is what makes the
two dead methods easy to miss.)

The live path, `Pages/Requests/Index.cshtml.cs:347`, does:
```csharp
r.Status = RequestStatus.Approved;
... SaveWithConcurrencyHandlingAsync ...
await _vacationApprovalService.ProcessApprovalSideEffectsAsync(id);
```
It sets the status **directly**, bypassing the entire approval state machine — including the
dual-approval threshold logic driven by `MoleculeApprovalSettings.DualApprovalDayThreshold`. A
30-day request that policy says needs parallel Lead + Director approval is approved by one click
from one Lead.

**Additional defect found while verifying (not in the original finding):** the status save is
COMMITTED before `ProcessApprovalSideEffectsAsync` runs, and that call is wrapped in a `try/catch`
that only LOGS (`LogTimeOffSideEffectsFailed`). A side-effect failure therefore leaves a
**permanently approved request with no side effects applied** — shifts not removed, notifications
not sent — with no retry and no user-visible error. Status and side effects are not in one
transaction.

**This is the 4th instance of the audit's meta-pattern** (a control that is built, tested and
provisioned but never wired): after `ViewAllShifts`, `BusyService.moleculeId`,
`ScopeFilterService.scopeId`.

## Unverified findings from the 3 dimensions that returned

`[ ]` = single-agent output, **not independently verified**. Each includes the auditor's own
attempt to refute itself.

### CRITICAL (4)

- `[ ]` **/Admin/Organization/Grants/Assign lets any AssignGrants holder mint an unscoped AdminAccess grant for themselves (full system takeover)**  
  `Pages/Admin/Organization/Grants/Assign.cshtml.cs:108` — kind=*security* — persona: Manager / Lead / BRDirector (any of the 95 seeded non-admin AssignGrants holders, e.g. test.manager@shifty.test)  
  **Failure:** test.manager@shifty.test (UserId 10, Role=Manager, company 32) holds AssignGrants (grant 36) via the Lead/Manager role template and no AdminAccess. They open /Admin/Organization/Grants/Assign and POST `SelectedUserId=10&SelectedGrantTypeId=57&CanOwn=true&CanGive=true` (57 = AdminAccess in the seeded DB) with every Scope* field left blank. A Grant row (UserId=10, GrantTypeId=57, all scope columns NULL, CanOwn=1) is inserted. From the next request on, every `HasGrantAsync(10,"AdminAccess")` call in the app returns true — Pages/Admin/Users.cshtml.cs:1273 `isAdmin` short-circuits all EditCompanyUsers company scoping, Pages/Requests/Index.cshtml.cs:789 ValidateAccessToRequestAsync returns true for every company, Pages/My/ApiKeys.cshtml.cs CheckIsOwnerAsync returns true, and Grant:AdminAccess pages (/Owner/Hub/**, RoleTemplates, DatabaseConsole) open. The DB copy shows 95 distinct users curren  
  **Auditor's self-refutation attempt:** I looked for (a) a CanGive/delegation check — Grants.cshtml.cs sibling page has `UpdateGrantDelegationAsync`, but Assign.cshtml.cs never reads CanGive of the caller's own grant; (b) a scope validator like ScopeFilterService — not injected here at all (only AppDbContext, ILogger, IJobTypeService, IAuditLogService, INotificationService, :20-24); (c) a global convention — Program.cs:112 is only `AuthorizeFolder("/")`; (d) a downstream guard that would neuter an all-null grant — the memory note says all-null grants are skipped in HasGrantWithScopeAsync, but the dangerous consumers here use the *un  
  **Proposed fix:** In OnPostAsync, before constructing the Grant: (1) resolve the target user and require `HasGrantForCompanyAsync(callerId, "AssignGrants", target.CompanyId)`; (2) require the caller to actually hold the grant being delegated with CanGive=true at a scope that *encloses* the requested Scope* tuple (walk Project>Area>Molecule>Company>Department); (3) reject an all-null scope unless the caller holds Ad

- `[ ]` **/Api/Hierarchy/Rename, /Delete and /Move mutate ANY project/area/molecule/company/department by id with no scope check (hard-deletes companies)**  
  `Pages/Api/Hierarchy/Delete.cshtml.cs:124` — kind=*security* — persona: MoleculeAdmin (מפק"מ) or AreaAdmin — ManageHierarchy is seeded ExpandToMolecule/ExpandToArea, so they legitimately reach only their own subtree  
  **Failure:** A MoleculeAdmin of molecule 'Tzafona' (ManageHierarchy scoped ExpandToMolecule to Tzafona only) POSTs `{"entityType":"company","entityId":<id of an empty/deactivated desk in a different area>}` to /Api/Hierarchy/Delete. The handler finds the company via IgnoreQueryFilters, sees no *active* users (MED-011 only counts `u.IsActive`, :131), wipes its DirectorCompanies rows and hard-deletes the Company row — a desk in an area they have no authority over disappears, along with the director mappings that gated access to it. The same actor can POST /Api/Hierarchy/Rename with `entityType=project` to rename the root Project of the whole deployment, or /Api/Hierarchy/Move with `entityType=company` to yank another area's desk into their own molecule, thereby bringing it inside their legitimate ManageHierarchy scope for every subsequent operation.  
  **Auditor's self-refutation attempt:** I checked whether the tenant query filter would block it — it cannot, every lookup is preceded by `.IgnoreQueryFilters()`. I checked whether the policy handler carries target scope — Authorization/GrantAuthorizationHandler.cs:40-46 builds GrantScope purely from `_currentUserService` (the caller's own Project/Area/Molecule/Company/JobType), so it proves only 'holds ManageHierarchy somewhere', never 'for THIS entity'. I checked for a shared base class or filter — these are plain PageModels with only AppDbContext/IAuditLogService/ILogger/IStringLocalizer injected. And Pages/Admin/Organization/Ind  
  **Proposed fix:** In each of Create/Rename/Delete/Move/Reorder, resolve the target entity's owning molecule (and for Move, the *destination* parent's molecule as well), then require `HasGrantWithScopeAsync(callerId, "ManageHierarchy"/"ReorderHierarchy", moleculeId/areaId/projectId: <resolved>)` — deny when the id cannot be resolved rather than falling through. Reuse the exact helper at Pages/Admin/Organization/Inde

- `[ ]` **The live approve/decline path bypasses the entire VacationApprovalService state machine — ApproveAsync/DeclineAsync have zero production callers**  
  `Pages/Requests/Index.cshtml.cs:347` — kind=*design-flaw* — persona: commander (Lead/Director) and soldier  
  **Failure:** A soldier in an Alhut molecule files a 30-day vacation. Per MoleculeApprovalSettings.DualApprovalDayThreshold=7 this requires parallel Lead+Director approval. In reality a single click by one Lead on /Requests sets Status=Approved; the second tier is never consulted, FirstApprovalActorId/SecondApprovalActorId stay NULL so the audit trail is empty, and the request never passes through PendingSecondApproval.  
  **Auditor's self-refutation attempt:** I looked for an alternative caller: a SignalR hub, a controller, a background job, or a second Razor handler that routes to ApproveAsync. I grepped `RequestStatus.Approved;` across all non-test, non-publish code — the only TimeOffRequest writers are Pages/Requests/Index.cshtml.cs:347, Services/Api/TimeOffApiService.cs:203 (API-key path, equally bypassing), and VacationApprovalService's own dead methods. I also checked whether ProcessApprovalSideEffectsAsync internally re-enters the state machine — it does not (VacationApprovalService.cs:1046-1096 only removes assignments, cancels shadowing, an  
  **Proposed fix:** Delete the inline status mutation in OnPostApproveTimeOffAsync/OnPostDeclineTimeOffAsync and delegate to `_vacationApprovalService.ApproveAsync(id, currentUserId)` / `DeclineAsync(id, currentUserId)`, surfacing the returned message key through the localizer. Do the same in TimeOffApiService. If ApproveAsync is genuinely not wanted, delete it and the PendingSecondApproval state rather than leaving 

- `[ ]` **Vacation approval is gated on ManagerHomeAccess, not ApproveVacations — job-type scoping on the vacation grant is never enforced**  
  `Pages/Requests/Index.cshtml.cs:775` — kind=*security* — persona: commander (Lead) approving outside their job-type scope  
  **Failure:** The Alhut Lead opens /Requests and sees — in the normal UI, no request forgery needed — the pending vacation of a Text (JobTypeId=3) soldier in company 1. Clicking Approve succeeds: RequireManagerAccessAsync passes (they hold ManagerHomeAccess somewhere), ValidateAccessToRequestAsync passes (same CompanyId), and their JobTypeId=1-pinned ApproveVacations grant is never read. They have just released a soldier from another vertical's coverage that they do not command. Symmetrically, a molecule/area-scoped ApproveVacations holder who lacks ManagerHomeAccess is refused entirely.  
  **Auditor's self-refutation attempt:** I checked whether `HasGrantAsync` silently resolves scope — it does not; the scoped overload is a different method (GrantService.cs:126-135). I checked whether the razor hides cross-job-type rows — Pages/Requests/Index.cshtml:495-544 renders every `Model.TimeOff` entry unfiltered. I checked whether GetPendingApprovalsForUserAsync (which does apply job-type scoping) feeds this list — it has no callers at all.  
  **Proposed fix:** Route the handler through ApproveAsync so CanUserApproveInternalAsync's `HasGrantWithScopeAsync(..., companyId, jobTypeId)` runs, and build the pending list from `GetPendingApprovalsForUserAsync(currentUserId)` instead of the raw company query, so what is displayed equals what is authorizable.


### HIGH (12)

- `[ ]` **Director "View as manager" mode is a fully dead control — the cookie is never read by any data path, but a persistent banner asserts the director is viewing another company**  
  `Services/ViewAsModeService.cs:32` — kind=*dead-control* — persona: Director / AreaAdmin overseeing several companies  
  **Failure:** A director assigned to companies 10 (home) and 42 opens /Director/ViewAsMode, picks company 42, and is redirected to /Calendar/Month. A yellow warning bar reads "Viewing as <company 42>" on every page for the next 8 hours. Every page still resolves its tenant from the director's own CompanyId claim, so the calendar, roster, requests queue and reports all show company 10. The director reviews "company 42's" week, sees it fully staffed, and signs off — while company 42's real week is unstaffed. The banner makes the wrong data look authoritative.  
  **Auditor's self-refutation attempt:** I looked for a TenantResolver rung, a middleware, or a page-level consumer that reads the view-as cookie and re-scopes data. CompanyContextMiddleware exists in Middleware/ but CompanyContext.CompanyId (Services/CompanyContext.cs:38-44) reads only `user.FindFirst("CompanyId")` — the claim. No IViewAsModeService injection exists outside _Layout and the ViewAsMode page itself. The gate inside EnterViewAsModeAsync (IsDirectorOfAsync, ViewAsModeService.cs:50-61) is correct but irrelevant: nothing downstream consumes the result.  
  **Proposed fix:** Either wire the view-as company into TenantResolver as an explicit rung (validated per request against IDirectorService.IsDirectorOfAsync, and marked read-only so mutating handlers refuse while it is active), or delete the feature end to end — the page, the service, the cookie and the banner. Do not leave a banner that asserts a scope the data layer does not honour.

- `[ ]` **Membership removal deletes the user's grants and shifts but never invalidates the active switched session — the removed member keeps full read access to that company for up to 12 hours**  
  `Services/CompanyMembershipService.cs:204` — kind=*security* — persona: Employee whose secondary-company membership was just revoked by an admin  
  **Failure:** User 611 is a member of companies 10 and 20 and is currently switched to 20 (member_selected_company=20). Company 20's admin removes the membership because the user left the desk — the handler deletes the user's company-20 grants and future shifts, so the admin reasonably believes access is cut. The user's browser is untouched: for the next 12 hours every tenant-filtered read still resolves to company 20, so /Calendar/Overview, /Calendar/Team, the roster, day-notes and time-off lists keep returning company 20's staffing data to a non-member. The admin has no way to force the session closed short of deactivating the whole account.  
  **Auditor's self-refutation attempt:** I checked whether the grant deletion alone closes the hole: it does not, because tenant-filtered READS are gated by the EF query filter `e.CompanyId == _tenantResolver.GetCurrentTenantId()` (Data/AppDbContext.cs:754, 858-997), not by grants — any page a plain employee can open renders company-20 data once the tenant resolves to 20. I also checked for a claims-refresh hook: Program.cs's AddCookie block (lines 160-200) configures only OnRedirectToLogin; no OnValidatePrincipal or ITicketStore exists, and SignInAsync appears only in Pages/Auth/Login.cshtml.cs:400 and Pages/Auth/GriffinCallback.csh  
  **Proposed fix:** On membership removal, revoke the session for that company: either drop TenantResolver's claim-snapshot check in favour of a per-request cached DB membership check, or add a `MembershipsChangedAt` stamp on AppUser plus a CookieAuthenticationEvents.OnValidatePrincipal that rejects the ticket when the stamp is newer than the ticket's issue time. At minimum, RemoveMembershipWithCleanupAsync must bump

- `[ ]` **MemberCompanyIds is baked only at login and only when the user already had 2+ memberships, so a membership added mid-session makes the company switcher silently no-op while the audit log records a successful switch**  
  `Pages/Auth/Login.cshtml.cs:393` — kind=*design-flaw* — persona: Employee just added to a second desk by an admin, still logged in  
  **Failure:** Employee 607 is logged in with a single membership (company 10), so no MemberCompanyIds claim exists in their 7-day sliding auth cookie. An admin adds them to company 20. The header switcher immediately renders both desks (live DB). The employee picks company 20; the POST succeeds, an audit row says "Member selected active company ID: 20", the page reloads — and everything still shows company 10, with the switcher label snapping back to "company 10" because ContextSwitcherViewComponent.cs:224 resolves the current context from `_tenantResolver.GetCurrentTenantId()`. The employee retries, concludes the app is broken, and files a ticket; the audit trail says the switch worked. The same happens to an existing 2-company member gaining a 3rd desk (claim holds the two old ids). Because SlidingExpiration is on, the stale claim can persist for weeks of continuous use.  
  **Auditor's self-refutation attempt:** I looked for a claim-refresh path that would repair the snapshot without logout: repo-wide `SignInAsync` appears only at Pages/Auth/Login.cshtml.cs:400 and Pages/Auth/GriffinCallback.cshtml.cs:249, and Program.cs's AddCookie block defines no OnValidatePrincipal. I also checked whether the membership-add handler forces a sign-out — Pages/Admin/Users.cshtml.Membership.cs has no such call. And the UI cannot mask it: the switcher's option list comes from the DB while its selected value comes from the resolver, which is exactly why the mismatch is visible.  
  **Proposed fix:** Stop making the switcher's authority a login-time snapshot. Either resolve membership per request (cache the id set in HttpContext.Items so the synchronous resolver stays cheap), or have SelectCompanyAsync — which already does the authoritative DB check — re-issue the auth ticket with a refreshed MemberCompanyIds claim, and always emit the claim (even for single-membership users) so a later additi

- `[ ]` **The company switcher is ignored by /Calendar/Month, /Calendar/Week and /Calendar/Day — ScopeFilterService resolves "company" scope from AppUser.CompanyId (the home pointer) instead of the tenant resolver**  
  `Services/ScopeFilterService.cs:104` — kind=*cross-feature* — persona: Multi-company member (e.g. a lead serving two desks) who switched to their secondary desk  
  **Failure:** A lead who is a member of desks 10 (home/primary) and 20 switches the header context to desk 20. They then click the "Calendars" breadcrumb (Pages/Calendar/Team.cshtml:17, Overview.cshtml:17, Shifts.cshtml:26 all point at /Calendar/Month) or a home-page card (Pages/Home/Index.cshtml:110, 221, 229, 269, 277). /Calendar/Month loads desk 10's shifts, chores and on-duty rows while the header switcher and every other calendar say "desk 20". The lead reads Monday as covered, but that is desk 10's Monday; desk 20's Monday is empty. Nothing on the page indicates the mismatch.  
  **Auditor's self-refutation attempt:** I checked whether the EF tenant filter would rescue this by intersecting to nothing (a visible failure rather than silently wrong data): it does not — LoadShiftsAsync/LoadChoresAsync call `.IgnoreQueryFilters()` explicitly and filter on the passed companyIds only. I checked whether these pages are unreachable legacy: FF_EXCEL_CALENDARS and FF_EXCEL_CALENDAR_SHIFTS are seeded enabled (Data/SeedData/FeatureFlagSeed.cs:34-35 via `F(...)` which sets `IsEnabled = true` at line 156, confirmed enabled in the e2e DB), so the primary nav item goes to /Calendar/Shifts — but /Calendar/Month remains the t  
  **Proposed fix:** Inject ITenantResolver into ScopeFilterService and resolve the "company"/"mine"/default branches from GetCurrentTenantId() (falling back to user.CompanyId only when it returns 0), and replace the `_companyContext.CompanyId` fallbacks in Day/Week/Month with the same resolver — mirroring the fix already applied to Overview.

- `[ ]` **/My/ApiKeys?handler=Refresh regenerates and discloses ANY colleague's API key plaintext (no ownership check)**  
  `Services/ApiKeyService.cs:314` — kind=*security* — persona: Ordinary Employee (page is [Authorize] only — no grant required)  
  **Failure:** An Employee with no grants at all opens /My/ApiKeys, notes their own key id (say 7), and POSTs `handler=Refresh&keyId=6`. Key 6 belongs to a manager and was approved with elevated scopes. The service finds it (same company, no owner predicate), overwrites its hash with a freshly generated key, and the page renders that plaintext into `GeneratedApiKey` for the Employee. The Employee now holds a working API key carrying the manager's scopes; the manager's integration silently breaks. Iterating keyId=1..N harvests every key in the desk. Note OnPostRefreshAsync also skips the `EnableApiKeyManagement` feature-flag check that OnGetAsync performs (:60-61), so the attack works even when the feature is disabled in the UI.  
  **Auditor's self-refutation attempt:** I looked for an ownership predicate in the service (none — the only predicate is CompanyId), for a guard in the page handler (it only TryParses the caller's own id and forwards keyId verbatim), and for an admin-only route split (there IS one — OnPostRevokeAdminAsync at :244 gates on CheckIsAdminAsync — which proves the non-admin OnPostRefresh/OnPostRevoke were meant to be self-only). I also checked whether the cshtml only renders the user's own key ids: it does, but that is a UI constraint, not an authorization one, and the handler accepts any integer.  
  **Proposed fix:** Add an ownership predicate to the self-service paths: resolve the ApiKeyRequest that produced the key and require `RequestedBy == callerId` (or add a `CreatedBy == callerId` clause) inside RegenerateApiKeyAsync/RevokeApiKeyAsync, or split them into `...ForOwnerAsync(keyId, callerId)` used by /My/ApiKeys and the existing unrestricted variants used only by the CheckIsAdminAsync-gated handlers. Also 

- `[ ]` **/Admin/Users password-reset, deactivate and delete handlers omit the target-privilege guard that the Membership/Move handlers on the same page enforce**  
  `Pages/Admin/Users.cshtml.cs:1985` — kind=*security* — persona: Company Manager holding EditCompanyUsers (95 seeded users hold it without AdminAccess)  
  **Failure:** A Manager in desk 32 holds EditCompanyUsers for desk 32 and no AdminAccess. An Owner/AreaAdmin account also sits in desk 32 (the seeded DB has exactly this shape: user 10 test.manager@shifty.test shares company 32 with an AdminAccess holder). The Manager POSTs to /Admin/Users?handler=ResetPassword with `id=<owner's id>&newPassword=Pwn12345`. `u.CompanyId == 32`, HasGrantForCompanyAsync passes, the hash is overwritten, and the Manager logs in as the Owner — inheriting AdminAccess and the entire deployment. Softer variants of the same hole: POST handler=Toggle with the Owner's id to deactivate them, POST handler=DeleteUser to purge them, or POST handler=Role with `id=<owner's id>&role=Employee` to demote them (CanAssignRoleAsync(Employee) succeeds for a Manager, and the target's current Owner role is never consulted).  
  **Auditor's self-refutation attempt:** I checked whether AuthorizeUserEditAsync (:1708-1728) — used by the DoesShifts/DoesChores/Gender handlers — adds the guard: it does not, it is the same isAdmin||EditCompanyUsers pair. I checked whether a self-target guard exists (Move has `if (userId == adminId) return "Error_MoveSelf";` at Move.cs:64) — no equivalent in reset/toggle/delete. I checked whether the password-reset notification (:2023-2026) or the audit log would block it — they only record it after the fact. The Membership/Move files prove the intended invariant exists and is simply not applied on these four handlers.  
  **Proposed fix:** Fold the guard into the shared helper: extend AuthorizeUserEditAsync to also require `await _directorService.CanAssignRoleAsync(u.Role)` (and reject `id == currentUserId` where self-action is nonsensical), then route OnPostResetPasswordAsync, OnPostToggleAsync, OnPostDeleteUserAsync and OnPostRoleAsync through it. In OnPostRoleAsync check BOTH `CanAssignRoleAsync(targetRole)` and `CanAssignRoleAsy

- `[ ]` **Admin/Organization Molecules, Areas, Projects, Departments and JobTypes rename/deactivate/delete any entity system-wide — the scoped check exists one directory up and is not used here**  
  `Pages/Admin/Organization/Molecules/Index.cshtml.cs:195` — kind=*security* — persona: MoleculeAdmin (EditMolecule/ManageDepartments ExpandToMolecule) or AreaAdmin (EditArea/ManageJobTypes ExpandToArea)  
  **Failure:** An AreaAdmin of area 'North' (EditArea ExpandToArea = North only) opens /Admin/Organization/Areas — the GET already lists every area in the deployment via `_db.Areas.IgnoreQueryFilters()` (:52) — clicks deactivate on area 'South' and POSTs `handler=ToggleActive&id=<South>`. `area.IsActive = !area.IsActive` fires with no scope check, and South's molecules/companies drop out of every `IsActive` filter across the app: NavRegistry entries, molecule pickers, JobType lists and calendar tab resolution for an entire foreign area go dark. A MoleculeAdmin can equivalently POST handler=Rename to Molecules with another molecule's EditId, or POST handler=Delete to Departments to remove a foreign molecule's department.  
  **Auditor's self-refutation attempt:** I checked whether the tenant query filter saves these — no, every lookup calls `.IgnoreQueryFilters()` first. I checked whether the policy attribute carries the target scope — Authorization/GrantAuthorizationHandler.cs:40-48 builds GrantScope from `_currentUserService` (the caller's own hierarchy) and calls HasGrantWithScopeAsync with it, so it proves only that the caller holds the grant for their OWN subtree. I checked the Create handlers, which at least resolve the parent (Molecules :119, Departments :118) — but they too only verify the parent EXISTS, never that the caller may write into it.  
  **Proposed fix:** Add a per-page `CanManageAsync(int scopeId)` helper mirroring Pages/Admin/Organization/Index.cshtml.cs:91-96 and call it at the top of every OnPost* after resolving the target: Molecules → `HasGrantWithScopeAsync(uid,"EditMolecule", moleculeId: molecule.Id)`; Areas → `areaId: area.Id` with "EditArea"; Departments → `moleculeId: department.MoleculeId`; JobTypes → `areaId: jobType.AreaId`; and for C

- `[ ]` **/Admin/HomeTypes edit/delete/toggle/generate/override act on any molecule's HomeType by id; Generate can overwrite a foreign molecule's HOME shifts for a year**  
  `Pages/Admin/HomeTypes/Index.cshtml.cs:263` — kind=*data-integrity* — persona: MoleculeAdmin holding ManageHomeTypes (molecule-scoped, 6 seeded holders, none with AdminAccess)  
  **Failure:** A MoleculeAdmin of molecule A enumerates HomeType ids (the GET already loads cross-molecule rows via `.IgnoreQueryFilters()`, :118-124) and POSTs `handler=Generate&GenerateHomeTypeId=<a HomeType in molecule B>&GenerateMode=overwrite&GenerateStartDate=2026-01-01&GenerateEndDate=2026-12-31`. GenerateHomeShiftsAsync runs in OverwriteAll mode against every user assigned to molecule B's home type, destroying a year of manually-adjusted HOME shifts for a molecule the actor has no authority over. The same actor can POST handler=Delete with molecule B's HomeType id (HomeTypeService.cs:79-90 nulls `u.HomeTypeId` for all its users and removes the row), or handler=ToggleActive to silently stop molecule B's rotation.  
  **Auditor's self-refutation attempt:** I checked whether the tenant query filter blocks the cross-molecule read — no, GetHomeTypeAsync and the page's own reads all call `.IgnoreQueryFilters()`. I checked whether the service validates scope internally: AssignUsersAsync DOES (HomeTypeService.cs:163-193 filters userIds to companies whose `c.MoleculeId == homeType.MoleculeId` and logs the excluded ones) — which proves molecule containment is a known concern here — but that guard constrains the USER list only; the homeTypeId itself is never checked against the caller, and Delete/Edit/Toggle/Generate/SaveOverride have no equivalent filte  
  **Proposed fix:** Add `private async Task<bool> CanManageMoleculeAsync(int moleculeId) => await _grantService.HasGrantWithScopeAsync(callerId, "ManageHomeTypes", moleculeId: moleculeId);` and call it in every OnPost* after resolving the target HomeType's MoleculeId (and for Create, against CreateMoleculeId), returning Forbid() when it fails or when the molecule cannot be resolved. Copy the shape of Pages/Admin/Orga

- `[ ]` **Approving a time-off request never materialises the calendar HOME rows — the leave is invisible on every calendar until the app restarts**  
  `Pages/Requests/Index.cshtml.cs:362` — kind=*implementation-bug* — persona: soldier who was granted leave, and the commander looking at the board  
  **Failure:** A commander approves Private Cohen's week of leave on Sunday. Cohen's existing shifts in that window are deleted (so the board now shows empty cells that the assigner will refill), but no vacation/HOME chip appears anywhere — /Calendar/Shifts, /Calendar/Team and Overview all show Cohen as simply unassigned and available. The assigner re-books him into the week he was just approved off. The chips only appear after an unrelated IIS app-pool recycle, and never at all if EndDate is more than 7 days in the past.  
  **Auditor's self-refutation attempt:** I checked whether a SignalR/calendar read path computes HOME rows on the fly from TimeOffRequests rather than from materialised ShiftAssignments — it does not: OverviewCalendarBuilder.cs:189, TeamCalendarEventAggregator.cs:499, Shifts.cshtml.cs:1273 and Chores.cshtml.cs:526 all read `ShiftAssignments ... .Include(sa => sa.SourceTimeOffRequest)`, i.e. the materialised rows. I also checked FF_HOME_UNIFICATION — it is seeded enabled (FeatureFlagSeed.cs:125), so the flag is not what suppresses it.  
  **Proposed fix:** Call `_materialiser.SyncMaterialisedHomeRowsAsync(id)` (gated on FF_HOME_UNIFICATION) immediately after the successful save in OnPostApproveTimeOffAsync and OnPostDeclineTimeOffAsync, or route both through ApproveAsync/DeclineAsync which already do it.

- `[ ]` **Deleting an approved time-off request leaves its materialised HOME rows on the calendar forever, silently reclassified as rotation HOME**  
  `Pages/Requests/Index.cshtml.cs:735` — kind=*data-integrity* — persona: commander revoking approved leave; soldier whose calendar is now wrong  
  **Failure:** A commander approves 10 days of leave, the rows get materialised (e.g. by the next restart), then the soldier's operation is cancelled and the commander deletes the approved request from /Requests. The TimeOffRequest disappears from every list and report, but 11 HOME ShiftAssignment rows remain on the board with the rotation icon and no way to trace them to a request. The soldier is shown at home for 10 days that were revoked, the ×-button on those chips now silently deletes single rows instead of opening the request dialog (calendar-inline-edit.js:1406 requires sourceRequestId > 0), and the shifts that ProcessApprovalSideEffectsAsync deleted at approval time are never restored.  
  **Auditor's self-refutation attempt:** I checked whether the delete is preceded by an un-materialise step elsewhere in the handler (lines 654-764) — it is not; the only post-save work is `CreateTimeOffDeletedNotificationAsync`. I checked whether SET NULL might instead cascade-delete — the migration explicitly writes ReferentialAction.SetNull, and even if SQLite FK enforcement were off the row would still survive with a dangling id. I checked whether the `StartDate <= today` guard (line 722) limits the damage — it only prevents deleting leave that has already begun, so a fully-materialised future leave is exactly the deletable case.  
  **Proposed fix:** Before removing the row, set `request.Status = RequestStatus.Canceled`, call `SyncMaterialisedHomeRowsAsync(id)` (which then deletes all rows for the request) and `RestoreRotationHomeAsync(request.UserId, request.StartDate, request.EndDate)` for Vacation/DayAt, all inside one transaction, and only then delete — or better, soft-cancel instead of hard-deleting so the audit trail survives.

- `[ ]` **"Cancel entire request" in the calendar dialog can never succeed — CancelRequestAsync rejects every status the dialog is reachable from**  
  `Services/VacationApprovalService.cs:800` — kind=*dead-control* — persona: soldier or commander undoing an approved leave  
  **Failure:** A commander approves the wrong soldier's vacation. Both the soldier and the commander click the × on the HOME chip, choose "בטל את כל הבקשה", and get a red error. For a single-day approved DayAt or After there is no Shorten button at all, so the dialog offers exactly one action and that action always fails — the approved leave is unrevocable from the calendar. The only escape is a manager using /Requests → Delete, which is itself limited to StartDate > today and orphans the calendar rows.  
  **Auditor's self-refutation attempt:** I checked whether some other handler intercepts Approved cancellations before CancelRequestAsync — Pages/Api/TimeOffRequest.cshtml.cs:71-91 calls it directly with no pre-processing. I checked whether the dialog is also reachable for Pending requests (which would make the button live at least sometimes) — it is not: the chip carries `data-source-request-id` only via the materialised ShiftAssignment, which exists only for Approved requests.  
  **Proposed fix:** Allow CancelRequestAsync to accept Approved requests (transitioning to Canceled) with the same un-materialise + RestoreRotationHome cleanup that CascadeGroupCancellationAsync already performs for Approved siblings (VacationApprovalService.cs:1451-1461), gated by the same owner-or-approver check UpdateRequestDatesAsync uses (line 862). Until then, hide the Cancel button when status is Approved.

- `[ ]` **Approved time off removes only shifts — chores and day-shifts (OnDuty) in the same window stay assigned, silently double-booking the soldier**  
  `Services/VacationApprovalService.cs:1061` — kind=*design-flaw* — persona: commander who must "approve without breaking coverage"; soldier who is on leave and still rostered  
  **Failure:** A soldier has a kitchen chore on Wednesday and is the Hakam day-shift on Thursday. Their week of leave is approved. Their Wednesday/Thursday *shifts* vanish, so the shifts board looks handled, but the chores board still shows them on Wednesday and the on-call board still shows them as Thursday's Hakam. Nobody is notified, no warning is shown to the approver, and the coverage hole is discovered on the day.  
  **Auditor's self-refutation attempt:** I searched for a downstream reconciler that clears chores/duties from an approved leave — grepping the chore and on-duty services for TimeOffRequest turned up only read-side joins (Pages/Calendar/Chores.cshtml.cs:526 `.Include(sa => sa.SourceTimeOffRequest)`), i.e. rendering, not clearing. I also checked whether the approver UI at least *warns*: Pages/Requests/Index.cshtml:495-544 renders only name, dates, duration and reason — there is no conflict or coverage panel.  
  **Proposed fix:** Extend ProcessApprovalSideEffectsAsync to remove (or flag) `Chores` and `OnDuties` for the same user/date range inside the same SaveChanges, and surface a pre-approval conflict summary on the approver card (shift count, chore count, duty count, and how many peers are already off that week) so the commander can judge coverage before clicking Approve.


### MEDIUM (8)

- `[ ]` **The director "Viewing N of M companies" scope control writes a cookie that no data query ever reads — changing scope changes nothing but the label**  
  `Services/CompanyFilterService.cs:36` — kind=*dead-control* — persona: Director overseeing 5 desks who narrows scope to one desk  
  **Failure:** A director responsible for 5 desks opens the header "Viewing 5 of 5" dropdown, unchecks four, and saves. The app confirms "company filter updated" and the header now reads "Viewing 1 of 5". Every calendar, roster and report still renders all 5 desks' data — the director scans what they believe is one desk's understaffed week and mis-attributes rows belonging to the other four. The control has no effect at all, and the success toast plus the changed label actively assert that it did.  
  **Auditor's self-refutation attempt:** I checked whether the filter is consumed indirectly through the tenant path: TenantResolver.GetCurrentTenantId() (Services/TenantResolver.cs:23-104) reads only the owner selector, the member cookie and the CompanyId claim — never director_company_filter. I checked whether the calendars consume it through ScopeFilterService instead: ScopeFilterService has no ICompanyFilterService dependency (constructor, Services/ScopeFilterService.cs:19-33) and its molecule/area branches enumerate companies straight from the hierarchy (lines 221-224, 262-265) with no filter intersection. The access validation   
  **Proposed fix:** Intersect the selected-company set into the data path — the natural seam is ScopeFilterService.ResolveCompanyIdsForScopeAsync, which should intersect its molecule/area result with GetSelectedCompanyIdsAsync() when that set is non-empty — or remove the control, the /Director/SetScope endpoint and the /Director/CompanyFilter page until a consumer exists.

- `[ ]` **/Admin/EditProfile is a second role/IsActive mutation path that skips the CanAssignRoleAsync gate — and permits self-promotion**  
  `Services/ProfileService.cs:312` — kind=*security* — persona: Manager / Lead holding EditCompanyUsers for their own desk  
  **Failure:** A Manager with EditCompanyUsers for desk 32 opens /Admin/EditProfile?UserId=<their own id>. CanEditTargetAsync returns true unconditionally on the self-branch (:61), and hasManagerPermissions is true because they hold EditCompanyUsers for their own company, so they POST the form with `Role=Owner`. `targetUser.Role = UserRole.Owner` is persisted. They are now un-manageable by their peers: Pages/Admin/Users.cshtml.Membership.cs:306 and Users.cshtml.Move.cs:94 both gate on `CanAssignRoleAsync(target.Role)`, and DirectorService.cs:131-146 lets nobody below AdminAccess assign Owner — so no other company admin can move them, alter their memberships, or (once the sibling handlers are fixed) touch them at all. The same POST against a colleague's UserId with `IsActive=false` deactivates any user in the desk regardless of their rank, again bypassing the CanAssignRoleAsync guard that /Admin/Users a  
  **Auditor's self-refutation attempt:** I first assumed Role=Owner would directly confer permissions and checked: it does not — authorization runs off Grant rows (GrantService.HasGrantAsync), and grep for `Role == UserRole.Owner` finds only notification-recipient and listing queries (NotificationService.cs:622, Companies.cshtml.cs:602, Login.cshtml.cs:449), so I downgraded the severity accordingly. But the finding survives: UserRole IS the input to CanAssignRoleAsync, which Membership.cs:306 and Move.cs:94 use as the anti-privilege-escalation gate, so writing your own Role is a real manipulation of the authorization graph, and the I  
  **Proposed fix:** In ProfileService.UpdateProfileAsync, before honouring dto.Role or dto.IsActive, require `await _directorService.CanAssignRoleAsync(dto.Role.Value)` AND `await _directorService.CanAssignRoleAsync(targetUser.Role)` (the current role, matching Users.cshtml.Move.cs:94), and reject Role/IsActive changes outright when `isEditingSelf` — nobody should edit their own rank or active flag through the profil

- `[ ]` **"Private" requests are enforced only as a list filter — every mutating handler and the read API ignore the flag, breaking the promise shown to the user**  
  `Pages/Requests/Index.cshtml.cs:201` — kind=*security* — persona: soldier filing a sensitive leave request  
  **Failure:** A soldier ticks "Private" and names one trusted Director as approver. Any other ManagerHomeAccess holder in the same desk, iterating /Api/TimeOffRequest/1..N (plain [Authorize], no scope check at all — even a non-manager can do this), reads the dates, type and status of the private request; and by POSTing that id to /Requests?handler=ApproveTimeOff they can approve or decline it, defeating the routing the soldier chose. The soldier sees the decision attributed to nobody, since FirstApprovalActorId is never populated on this path.  
  **Auditor's self-refutation attempt:** I checked whether ApiAuthenticationMiddleware adds a scope check for /Api/TimeOffRequest — the class comment (Pages/Api/TimeOffRequest.cshtml.cs:19-21) says the middleware only enforces the X-Requested-With CSRF header, and the code's own justification ("the calendar already enforces visibility") is a client-side claim, not a server check. I checked whether the dead ApproveAsync honours Private — it does not either, so wiring it up would not by itself close this.  
  **Proposed fix:** Move the privacy predicate into a shared server-side authorization helper used by the GET API and by every approve/decline/delete handler: if `r.Private` then require `currentUserId == r.ApproverId` (or AdminAccess). Add the same owner/approver scope check to `OnGetAsync` on the API page.

- `[ ]` **The stale-request reaper silently cancels pending leave filed more than 30 days ahead, with no notification to the soldier**  
  `Services/StaleRequestReaperJob.cs:75` — kind=*design-flaw* — persona: soldier planning leave in advance  
  **Failure:** On 1 January a soldier files leave for 1–10 April (a 90-day lead time, normal for a long vacation). On 31 January the weekly reaper flips it to Canceled. No notification, no audit entry, no reason. The soldier believes it is still pending; the commander's queue silently loses it; on 1 April the soldier is still on the roster. When the soldier eventually checks /My/Requests they see "בוטלה" and reasonably conclude they cancelled it themselves.  
  **Auditor's self-refutation attempt:** I checked whether the reaper is disabled by default — `Reaper:IntervalDays` defaults to 7 and `Reaper:MaxAgeDays` to 30 with no enable flag, and it is a `BackgroundService`, so it runs unconditionally. I checked whether a notification is raised by a SaveChanges interceptor rather than explicitly — the only notification calls for TimeOffRequest are in VacationApprovalService.cs:1093 and Pages/Requests/Index.cshtml.cs:441/747, none of which the reaper touches.  
  **Proposed fix:** Reap on `EndDate < today` (a request whose window has passed is genuinely stale) rather than on CreatedAt age, introduce a distinct terminal status (e.g. Expired) so it is not confused with a user cancellation, and notify the requester and the approver pool when it fires.

- `[ ]` **The soldier's swap-request history renders hardcoded placeholder data — every row shows today's date and the literal string "Swap Request"**  
  `Pages/My/Requests.cshtml.cs:129` — kind=*implementation-bug* — persona: soldier tracking "where does my request stand"  
  **Failure:** A soldier who filed three swap requests for three different shifts opens /My/Requests. All three rows show today's date and the caption "Swap Request" — in English, on a Hebrew-first RTL page — with an untranslated English status badge. There is no way to tell which shift each row refers to, which is precisely the question the page exists to answer.  
  **Auditor's self-refutation attempt:** I checked whether the razor overrides these fields with a client-side lookup — Pages/My/Requests.cshtml:170-192 renders the model properties directly and the page's only <script> block (in _TimeOffRequestForm.cshtml) toggles form fields. I checked whether MySwapRequests is repopulated by a later handler — OnGetAsync is the only writer.  
  **Proposed fix:** Replace the placeholder projection with the same SwapRequests→ShiftAssignments→ShiftInstances→ShiftTypes join used at Pages/Requests/Index.cshtml.cs:137-151 (resolving the shift-type name through ICompanyLocalizationService as OnGetAsync already does for AvailableShifts), and change line 183 to `@Localizer[request.Status]`.

- `[ ]` **The cancel/shorten dialog shows raw resource keys to Hebrew users; two keys used by the shorten path do not exist in either resx**  
  `Pages/Api/TimeOffRequest.cshtml.cs:87` — kind=*localization-rtl* — persona: Hebrew-first soldier or commander  
  **Failure:** A Hebrew-speaking soldier clicks × on a HOME chip and chooses "בטל את כל הבקשה". Because that path always fails (see the dead-control finding), the red banner reads the ASCII string "VacationApproval_AlreadyProcessed" in an RTL layout. If a commander outside scope tries to shorten someone's leave, the banner reads "VacationApproval_NotYourRequest"; any internal exception during a shorten shows "VacationApproval_Error".  
  **Auditor's self-refutation attempt:** I checked whether the JS maps keys to strings before display — cancel-or-shorten-dialog.js has only the two-arg `t(en, he)` helper for its own literals and no key lookup table. I checked whether the API localizes on the way out — TimeOffRequestApiModel injects AppDbContext, IVacationApprovalService and ILogger only (lines 27-39); there is no IStringLocalizer.  
  **Proposed fix:** Inject IStringLocalizer into TimeOffRequestApiModel and resolve the returned key before serialising (matching what UpdateRequestDatesAsync already attempts), and add the missing `VacationApproval_NotYourRequest` / `VacationApproval_Error` entries to both resx files. Pick one convention — return keys everywhere and localize at the edge, or localize in the service — rather than the current mix.

- `[ ]` **The admin-configurable "auto-approve up to N days" rule is computed and then discarded — it never approves anything**  
  `Services/VacationApprovalService.cs:135` — kind=*dead-control* — persona: admin configuring approval policy; soldier expecting a short leave to clear itself  
  **Failure:** An admin sets MaxAutoApproveDays = 2 for their desk so single-day "After" requests clear without a commander. Soldiers file them and they sit in Pending indefinitely; the commander's queue fills with requests that policy says should never have reached them, and 30 days later the reaper silently cancels the ones nobody got to.  
  **Auditor's self-refutation attempt:** I checked whether auto-approve is applied by the caller instead — Pages/My/Requests.cshtml.cs:306-316 only calls SubmitForApprovalAsync and logs the returned message; it never inspects the route. I checked whether the seeded rule set makes this moot — VacationApprovalRules is empty (0 rows) in the e2e DB, so no one has hit it yet, but the control is live in the admin UI and will silently no-op the first time it is used.  
  **Proposed fix:** Have GetApprovalRouteAsync return an explicit `AutoApprove` flag (rather than overloading a null ApproverId) and have SubmitForApprovalAsync, when it is set, drive the request to Approved through the same code path as a real approval — status transition, ProcessApprovalSideEffectsAsync, materialiser, LeaveGroup cascade — and emit the already-translated VacationApproval_AutoApproved message.

- `[ ]` **The approver card omits the request type and label, so a commander cannot tell a vacation from an After or a Day-at**  
  `Pages/Requests/Index.cshtml.cs:68` — kind=*ux-discoverability* — persona: commander deciding; soldier tracking  
  **Failure:** Two requests arrive for the same single day: one is an After (soldier leaves at 16:00 and returns 13:00 next day) and one is a full-day Day-at "ים". Both cards read identically — same name, "1 יום", same dates. The commander approves what they believe is a half-day release and actually clears two consecutive half-days of coverage. The soldier, checking /My/Requests, likewise cannot confirm which type was filed or that their "ים" label survived.  
  **Auditor's self-refutation attempt:** I checked whether Type is inferable from the rendered duration — no: an After and a one-day DayAt and a one-day Vacation all render `(EndDate.DayNumber - StartDate.DayNumber + 1)` = 1 day (Pages/Requests/Index.cshtml:514). I checked whether the type appears in the submit-notification the approver receives — Notif_TimeOffRequestSubmittedMessage is formatted with the requester name only (VacationApprovalService.cs:178).  
  **Proposed fix:** Add `Type` and `Label` to TimeOffVM and MyTimeOffRequest, render a localized type chip (חופש / אפטר / יום ב-{Label}) on both the approver card and the soldier's history row, and include the actual time window (16:00→13:00 for After) so the coverage impact is legible before approving.


### LOW (1)

- `[ ]` **GET /Api/TimeOffRequest/{id} returns any user's leave type, dates and status to any authenticated caller**  
  `Pages/Api/TimeOffRequest.cshtml.cs:47` — kind=*security* — persona: Any authenticated user, including a Trainee  
  **Failure:** A Trainee in desk 1 issues `GET /Api/TimeOffRequest/4821` with the `X-Requested-With` header. The handler ignores tenant filters, finds a request belonging to an employee in a different molecule, and returns its Type (e.g. an extended-leave/medical category), StartDate, EndDate and Status. Iterating ids 1..N enumerates the leave calendar of every person in the deployment, across every company — including who is away and for how long, which for this air-gapped deployment is exactly the schedule information the tenant boundary exists to protect.  
  **Auditor's self-refutation attempt:** I verified the two mutating handlers on the same endpoint are NOT vulnerable — VacationApprovalService.cs:798 `return (false, "VacationApproval_NotYourRequest")` on an owner mismatch for cancel, and :862 `if (req.UserId != actorUserId && !await CanUserApproveAsync(actorUserId, req.Id))` for update-dates — so this is a read-only leak, not a write. I looked for a middleware-level filter on /Api/TimeOffRequest: ApiAuthenticationMiddleware whitelists it for the X-Requested-With CSRF check only, which authenticates nothing about the target. I considered whether the id is unguessable — it is a seque  
  **Proposed fix:** Add the same ownership/approver predicate the POST handlers already use: after loading the request, allow only when `r.UserId == callerId` or `await _svc.CanUserApproveAsync(callerId, r.Id)` (or the caller holds ViewVacations for `r.CompanyId` via GetAccessibleCompanyIdsForGrantAsync); otherwise return 404 so the endpoint does not confirm the id exists.


## Still not covered

- **`/Calendar/Chores` — FOUR failed attempts, still completely unaudited.**
- **RoleTemplate coherence / auto-grant lifecycle / seed id-vs-comment integrity** — never ran.
  (The seed-id check matters: `RoleTemplateSeed` references grants by NUMERIC id with a naming
  comment, and `GrantTypeSeed` assigns ids by sequential `id++`. A drift there silently grants a
  different permission than the comment claims.)
- `Pages/Owner/Backup.cshtml.cs` restore/delete handlers (client-supplied ids) — listed, never read.
- `Pages/Requests/Swaps/*` — never opened.
- Nothing in this pass was click-verified at runtime; no test was run against any finding.
