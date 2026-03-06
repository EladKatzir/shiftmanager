1) What ShiftManager is (in human terms)

ShiftManager is a workforce scheduling and coordination system designed for environments where security and offline operation matter a lot.

What it helps people do

Plan shifts (who works when)

Track shift types (day/night/etc.)

Manage time-off requests

Manage shift swap requests (two people exchanging shifts)

Manage chores and on-duty schedules

Send notifications (and optionally emails)

Keep an audit trail (a record of “who changed what and when”)

Who it’s built for

The docs explicitly target places like:

Military units / government orgs

Hospitals / emergency services

Critical infrastructure (power plants, data centers)

Secure manufacturing / defense contractors

These environments often have:

Strict security rules

Minimal or no internet

Windows desktops in a controlled network

USB transfer as the only way to move files in/out

2) The defining constraint: “Air-gapped” deployment

Air-gapped (plain English): the computers running the system are physically isolated from the internet (or any “unsafe” network). Think of it as a room with no door to the outside world—you can’t just “install updates from the web” or “use cloud services”.

What this forces the design to look like

ShiftManager is intentionally built to be:

Self-contained deployment (plain English): everything needed is included in the install package—no external downloads.
Example: it ships with the .NET runtime, the app, and the database engine.

No build step for the UI (plain English): no complex “frontend tooling” like npm/webpack. You can copy files and run.

SQLite database (plain English): the database is a single file on disk (like a strong, structured Excel file), not a separate database server.

3) Big-picture architecture: “Layers” (like floors in a building)

ShiftManager is built as a monolithic layered application.

Monolithic (plain English): it’s one application (not a constellation of many tiny services).

Layered architecture (plain English): the app is organized into “floors,” where each floor has a job:

UI floor

API floor

Business rules floor

Database access floor

Database storage floor

This matters because it keeps complexity manageable in a secure/offline environment.

4) The core moving parts (overview)

Think of ShiftManager as a restaurant:

UI (Razor Pages) = the dining room + menus (what users see)

API = the “takeout counter” for external systems (automated clients)

Service Layer = the kitchen (where rules and decisions happen)

Database Layer (EF Core + SQLite) = pantry + records room (where data lives)

Now let’s define those terms:

ASP.NET Core

ASP.NET Core (plain English): Microsoft’s framework for building web applications. It’s the “engine” ShiftManager runs on.

Razor Pages

Razor Pages (plain English): a way to build web pages where each page can have:

the HTML (what it looks like), and

a page model (server logic for that page)

So: a page is both the screen AND the code behind it.

REST API

REST API (plain English): a standard way for other software to talk to ShiftManager using web requests (HTTP).
Example: a Python script can request /api/v1/shifts and get a list of shifts.

Service Layer

Service layer (plain English): code that contains business logic—the real rules of how the organization operates.
Example: “You can’t approve time-off if it conflicts with mandatory coverage rules.”

EF Core (ORM)

EF Core is an ORM.

ORM (Object-Relational Mapper) (plain English): a translator that lets developers work with database data as normal objects instead of writing raw SQL all the time.

It enforces rules like CompanyId filtering (explained next) and handles migrations.

SQLite

SQLite (plain English): a database stored in a single file (great for offline installs).

5) The single most important concept: Multi-tenancy (Company isolation)

ShiftManager supports multiple organizations (called “companies” in the docs) inside one installation.

Multi-tenancy

Multi-tenancy (plain English): one copy of the app can serve many separate groups, and each group’s data must be isolated.

Analogy: one hotel building with many rooms—each guest can’t enter other guests’ rooms.

The core isolation mechanism: CompanyId

CompanyId (plain English): every “tenant-owned” row in the database has a label saying “this belongs to Company #7”.

So most tables include a CompanyId column.

6) How tenant isolation is actually enforced (the “3-layer lock”)

ShiftManager uses multiple safety layers to prevent cross-company data leaks.

6.1 Global Query Filters

Global Query Filter (plain English): a rule that EF Core automatically applies to every query, like silently adding:

“Only show rows where CompanyId = the user’s CompanyId”

So even if a developer forgets to filter, EF Core applies it.

6.2 SaveChanges Interceptor (CompanyId Interceptor)

Interceptor (plain English): a hook that runs automatically when saving to the database.

CompanyId interceptor (plain English): when a new row is inserted, it automatically sets the CompanyId so developers don’t forget.

6.3 Claims-based tenant resolution

This depends on authentication.

Claim (plain English): a piece of data attached to your login session, like:

your UserId

your role (Manager/Employee/etc.)

your CompanyId

The TenantResolver reads the claim and says: “you are in Company #X”.

7) Exceptions to tenant isolation (intentional “global” tables)

Not every table is filtered by CompanyId. The docs explicitly call out exceptions.

Global tables

Global table (plain English): data shared across all companies (no CompanyId column).

Example from the docs:

OnDuty / OnDutyTypeConfig are described as global/public for cross-company visibility.

Cross-tenant mapping table

DirectorCompany is a special mapping table:

It links a Director to the companies they oversee

It intentionally does not use a global query filter

Plain English: it’s the “permission map” that tells which companies a Director can access.

8) User roles and who can do what

ShiftManager uses role-based access.

Role hierarchy (from the docs)

Owner (highest)

Director (manages multiple companies)

Manager (manages one company)

Assigner (chores only)

Employee

Trainee (limited/shadowing)

Role-based authorization

Authorization (plain English): deciding what you’re allowed to do after you log in.

ShiftManager uses:

Policies (plain English): named rules like “Only Managers can approve requests.”

9) Authentication: how users log in
Authentication

Authentication (plain English): proving who you are (login).

ShiftManager supports two main paths:

Standard login (username/password)

Griffin ADFS Single Sign-On (SSO)

9.1 Standard login

Important security pieces here:

Password hashing (PBKDF2)

ShiftManager uses PBKDF2.

Hashing (plain English): converting a password into a “fingerprint” that can’t realistically be reversed.

PBKDF2 (plain English): a secure method for creating that fingerprint by doing many repeated steps to make cracking hard.

Rate limiting

Rate limiting (plain English): blocking too many login attempts from the same IP to reduce password guessing attacks.
Docs mention a rule like “10 attempts per 15 minutes per IP”.

Session management

Session (plain English): your “logged-in state” stored in a secure cookie.

Cookie (plain English): a small piece of data stored in your browser that says “this person is logged in”.

9.2 Griffin ADFS integration (SAML SSO)

This is for secure environments.

ADFS

ADFS (plain English): Microsoft’s enterprise login system. It can log you into apps using the same identity you use for the organization.

SSO

SSO (Single Sign-On) (plain English): you log in once (to your organization), and the app trusts that identity.

SAML

SAML (plain English): a standard “login passport format” for SSO.
ADFS sends ShiftManager a signed identity “ticket” with claims like username, roles, etc.

10) The UI/UX architecture: why it’s “simple” on purpose

ShiftManager’s UI approach is intentional:

No build process

Plain English: the system avoids heavy frontend tooling.

The docs emphasize:

No Bootstrap

No Tailwind

No npm packages

Custom CSS and vanilla JavaScript

Why this matters in air-gapped environments

Because you can’t reliably:

install packages from the internet

run complicated build pipelines on a locked-down machine

troubleshoot dependency chain issues

So the UI is:

stable

deployable by copying files

predictable

Key UI statistics (from the docs)

66 Razor Pages

~4,700 lines of CSS

~4,200 lines of vanilla JavaScript

0 npm packages

111MB deployment (because it’s self-contained)

11) UI areas (what screens exist)

ShiftManager is organized into functional page folders.

Auth

Login, signup, logout, forgot password, ADFS callback.

My (employee self-service)

Personal dashboard, profile edit, requests, notification center, API keys.

Calendar

Month / week / day / table view scheduling.

Requests

Time-off creation, swap creation, request overview.

Assignments

Shift assignment management.

Public

Chores calendar, on-duty calendar, feedback form (these are “public views” in-app—still inside the system, just broadly visible).

Admin / Owner / Director

User management

Company management

Director assignment

Shift type configuration

Feature flags

Health diagnostics

Backup & database console

Director cross-company tools

12) Localization and RTL (Hebrew support)

ShiftManager supports:

Localization (plain English): multiple languages.

RTL (Right-to-Left) (plain English): Hebrew/Arabic style text direction.

How localization works here

ASP.NET Core uses:

Resource files (plain English): translation dictionaries stored as files.

Keys like "Error_Login_RateLimitExceeded" map to Hebrew/English strings.

Why it’s more than just translation

In RTL languages:

layout flips direction

icons and alignment need changes

CSS needs overrides

The docs describe:

Conditional loading of rtl.css

A localization approach that also handles JS strings and HTML attributes (tooltips/placeholders)

13) Data model: the “things” the system stores (domain models)

A domain model (plain English): the list of “real-world objects” the software tracks.

Below are the major ones from the documentation, in plain language.

Core domain entities
Company

A tenant/organization in the system.

AppUser

A person using the system (employee/manager/etc.)

Shift management entities
ShiftType

A reusable definition like “Night Shift” or “Morning Shift”.

ShiftInstance

A specific date/time occurrence of a shift.

ShiftAssignment

Who is assigned to which shift instance.

Request workflow entities
TimeOffRequest

Someone asks for vacation/sick leave/etc., usually needing approval.

SwapRequest

Someone asks to swap shifts with someone else, usually needing approval.

UserJoinRequest

Someone requests to join a company (like “please add me to Unit X”).

Task assignment entities
Chore

Recurring chores/tasks to assign (cleaning duty, paperwork duty, etc.)

OnDuty

The on-duty roster/calendar (often treated as broader visibility)

Notifications
UserNotification

A notification item for a user.

DailyNotificationPreference

Settings for digest emails/notifications.

Audit & compliance
AuditLog (and audit-related entities)

A tamper-evident-ish trail of important actions:

approvals

assignments

role changes

Plain English: “the black box recorder”.

Configuration entities
AppConfig / EmailConfig / GriffinConfig

Settings for system behavior, email integration, and Griffin ADFS.

EmailApiLog / GriffinApiLog

Logs of calls made to those external systems (useful for troubleshooting).

API infrastructure entities
ApiKey

A “secret token” a script can use to call the API.

API key (plain English): like a long password used by software, not a human.

ApiRequestLog

Logs of API calls (note: docs mention special handling, including cases with no query filter).

14) Service layer: where “the rules” live

The docs emphasize a “thick service layer”.

Thick service layer (plain English)

Instead of putting rules inside the UI pages or raw database calls, ShiftManager centralizes rules in services.

Why this is good:

rules are consistent

easier to test

safer (less chance a page bypasses rules)

Service stats (from docs)

63 total services

Many interface-based patterns

Interface

Interface (plain English): a “contract” that says what a service can do, without locking you to one implementation.
It makes testing easier because you can substitute fake versions.

15) API layer: external access (for scripts/integrations)

ShiftManager provides an external REST API v1 with controllers like:

Users

Shifts

Time-off

Notifications

Swap requests

Chores

On-duty

Feedback

Controller

Controller (plain English): a group of endpoints (URLs) that handle requests.
Example: ShiftsController handles /api/v1/shifts/...

Dual auth model

Plain English: browsers log in with sessions/cookies, but external clients often use API keys.

16) Middleware pipeline: the “security checkpoints” every request passes through

Middleware (plain English): a chain of steps that runs for every request, like an airport security line.

Examples of what middleware can do:

detect language

validate session

set company context (tenant)

enforce security headers

A key middleware here is:

CompanyContextMiddleware

Plain English: it figures out “which company is this request operating under?” and sets the context so filtering and rules work correctly.

17) Workflows (real business processes)

A workflow (plain English): a multi-step process involving rules, approvals, and side effects (like notifications).

ShiftManager documents workflows for:

Shift assignment workflow

assign a user to a shift

validate conflicts/rest rules

persist assignment

notify people

Time-off request workflow

user submits request

manager reviews

approve/decline

update schedule impact

notify user

log audit event

Swap request workflow

user initiates swap

system checks feasibility

manager approves/declines

assignment is moved

notifications sent

Notification digest workflow

background job runs daily

aggregates relevant items

sends digest based on preferences

18) Email template customization (owner-controlled messaging)

This is a major feature in the docs.

Email template customization

Plain English: the Owner can edit “what emails look like” (subject/body) for different notification types, without changing code.

Think: “editable message templates” stored in the database.

Why it matters

In military/government contexts, email formats often must match strict norms:

required wording

bilingual formatting

consistent subject lines

The docs include:

database table for template customization

service layer that selects the right template

reset-to-default behavior

validation and audit trail

19) Configuration UI enhancements (Griffin & Email)

This is about making configuration safer and clearer.

Key ideas:

status widgets (visual “is it working?” indicators)

real-time validation (warn you while typing)

diagnostic console (help debug integration issues)

RTL support for these widgets too

Plain English: configuration screens that behave like a “guided setup wizard” rather than a scary raw settings page.

20) Caching strategy: speeding things up without breaking correctness
Cache

Cache (plain English): short-term memory to avoid repeated expensive work.

Example: shift types don’t change often, so you can store them in memory instead of reading from the database every time.

ShiftManager uses:

IMemoryCache (plain English): in-process memory cache inside the app.

Cache-aside pattern

Plain English:

Check cache first

If missing, load from DB

Store result in cache

Cache invalidation

Invalidation (plain English): clearing the cache when the underlying data changes so users don’t see stale results.

Two types used:

manual invalidation (clear when saving)

TTL expiration

TTL

TTL (Time To Live) (plain English): how long cached data is allowed to live before expiring automatically.

21) Data migrations: evolving the database safely
Migration

Migration (plain English): a versioned “upgrade step” that changes the database structure or data.

Example: adding a new table, adding a new column, or backfilling values.

The docs treat migrations as critical because:

the system may be upgraded via USB

machines might have older data files

errors must be recoverable

22) Data lifecycle management: archive, purge, import (big deal in secure orgs)

This document is basically: “How do we handle years of operational data safely?”

Data lifecycle management

Plain English: rules and tools for:

archiving old data

purging (permanently deleting) when required

exporting/importing data (for disaster recovery, mergers, etc.)

The docs describe:

a three-service architecture for lifecycle operations

safety philosophy (avoid accidental destruction)

“typed confirmation” (forcing a human to type something to confirm destructive actions)

Archive formats: CSV + NDJSON

CSV (plain English): spreadsheet-friendly format (comma-separated values).

NDJSON (plain English): “newline-delimited JSON” — each line is one JSON record. It’s easier for machines to stream and re-import reliably.

Why two formats:

CSV helps humans inspect

NDJSON helps machines restore precisely

23) Deployment in air-gapped Windows environments (very practical details)
The “Zone.Identifier” problem

Windows can mark files downloaded from elsewhere as “from the internet”.

Zone.Identifier (plain English): a hidden Windows tag that says “this file came from outside”. In locked-down environments, Windows may block DLLs (components) from loading.

So when you copy via USB, you may need “unblocking” steps.

Self-contained deployment packaging

Self-contained (plain English): the app ships with the runtime, so the target machine doesn’t need to install .NET separately.

24) Build & release pipeline (how the software is produced)
Build pipeline

Plain English: the standardized steps to create a deployable package:

restore dependencies

compile

run tests

publish

package into a USB-transferable bundle

checksum generation

SHA256 checksum

Checksum (plain English): a file fingerprint used to verify integrity.
It helps prove “this package wasn’t corrupted or tampered with during transfer”.

25) Testing strategy (how correctness is proven)

ShiftManager documents testing across:

Unit tests (plain English): test one small piece in isolation.

Integration tests (plain English): test that multiple pieces work together (e.g., service + database).

Manual test checklists (plain English): repeatable human testing steps.

26) Design decisions & tradeoffs (why it’s built this way)

This is the “why” document.

Common tradeoffs you’ll see (in plain terms):

Monolith vs microservices: chose monolith for simplicity/offline constraints.

SQLite vs SQL Server: chose SQLite because it’s file-based, deployable, and doesn’t require server infrastructure.

No frontend build: chosen due to air-gapped constraints and reliability.

Razor Pages: simple deployment, server-rendered pages, minimal JS required.

27) Reconstruction recipe (how to rebuild the project from scratch)

This document is essentially a cookbook:

exact steps to create the solution

the order to add modules

scripts to build packages

verification checklist

Plain English: if you lost the repo, you could rebuild it systematically.

28) ADFS Integration Analysis report (what’s strong, what’s missing, what to improve)

This report answers:

how authorization should work with claims

how multi-tenancy should be handled with ADFS

what gaps exist (compliance, audit, config management, etc.)

prioritized recommendations

Plain English: it’s a “security and completeness review” of the ADFS approach.

29) A very concrete “walkthrough”: what happens when a user requests time-off

Let’s trace an example end-to-end.

Step A: User submits a request in the browser

They open the Time-Off Create page (Razor Page).

They fill a form and click submit.

Step B: Server receives the request

Request enters ASP.NET Core and passes through middleware (security/language/tenant context).

Step C: Tenant is determined

The system reads the user’s session claims.

It sets the current CompanyId context.

Step D: Business rules are applied

A service (service layer) checks things like:

Is the request valid?

Does it overlap with other requests?

Does the user have the required role?

Step E: Data is saved safely

EF Core writes the TimeOffRequest into SQLite.

The CompanyId interceptor ensures the row is labeled with the right CompanyId.

Step F: Notifications and audit logs

A notification is created for the manager.

An audit record is written (“User X requested time-off”).

Step G: Optional email delivery

If email is configured:

the system selects the correct template (possibly customized)

sends via the configured email mechanism

logs email API interaction

Step H: Manager approves

Manager sees request, approves/declines.

Another workflow runs (updates status, logs audit, notifies user).

That’s the overall “machine”.

30) Glossary (plain English cheat sheet)

Here’s a compact reference you can return to while reading:

Air-gapped: physically offline from the internet.

Tenant / Company: one organization using the system.

Multi-tenancy: multiple companies share one app instance but must stay isolated.

CompanyId: the label on data rows that says which company owns it.

Database: structured storage (ShiftManager uses SQLite—a single file DB).

Table: a category of stored records (like a spreadsheet sheet, but stricter).

Row: one record in a table.

Primary Key (PK): the unique ID of a row.

Foreign Key (FK): a reference linking one row to another (like a pointer).

ASP.NET Core: the web application engine.

Razor Pages: web pages + server logic per page.

API: a machine-to-machine interface.

REST: a standard style of API using HTTP requests.

Endpoint: a specific API URL path.

Service layer: where the rules live.

Middleware: steps that every request passes through (security checkpoint chain).

EF Core / ORM: translator between code objects and database tables.

Migration: a versioned upgrade to the database structure/data.

Authentication: proving who you are (login).

Authorization: deciding what you’re allowed to do.

Role: a permission category (Owner/Director/Manager/etc.).

Claim: a fact in your login ticket (CompanyId, Role, UserId).

SSO: single sign-on (login once via organization).

ADFS: Microsoft’s enterprise login service.

SAML: standard identity “passport” format for SSO.

PBKDF2: secure password hashing method.

Hashing: turning a password into a non-reversible fingerprint.

Rate limiting: blocking too many attempts to prevent abuse.

Cache: short-term memory for speed.

TTL: automatic cache expiration time.

Audit log: tamper-resistant-ish record of actions