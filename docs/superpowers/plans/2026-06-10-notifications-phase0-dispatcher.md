# Notifications Overhaul — Phase 0: Dispatcher Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a centralized `NotificationDispatcher` + typed `NotificationEvent` catalogue that, for now, delegates to the existing `INotificationService` methods — establishing the Approach-A front door without changing any runtime behavior (Approach-C migration step).

**Architecture:** A new `Services/Notifications/` namespace holds an abstract `NotificationEvent` record carrying declarative metadata (category, personally-actionable, security-critical, calendar-eligible, batchable) plus concrete event records. `INotificationDispatcher.RaiseAsync(evt)` switches on the concrete event type and calls today's `INotificationService` creators (which already persist in-app + send email). Nothing in production calls the dispatcher yet — Phase 0 is purely additive and independently testable. Phase 2 will make the dispatcher *consume* the metadata; Phase 3 will migrate call sites onto it.

**Tech Stack:** ASP.NET Core 8.0, C# records, xUnit + FluentAssertions + Moq, EF Core/SQLite.

**Spec:** `docs/superpowers/specs/2026-06-10-notifications-overhaul-design.md` (§5.1–5.4).

> **Concurrency note:** Another Claude Code session may be running. Do NOT rebuild or kill processes while it may hold the binary (per the project executable-lock policy). All Phase 0 files are *new* except a 1-line DI edit in `Program.cs` — coordinate that single edit to avoid a merge conflict.

---

## File Structure

| File | Responsibility |
|---|---|
| `Services/Notifications/NotificationCategory.cs` | Enum of notification categories (used by prefs/mutes in Phase 2) |
| `Services/Notifications/IcsMethod.cs` | Enum: Request / Update / Cancel (calendar metadata, consumed Phase 4–5) |
| `Services/Notifications/NotificationEvent.cs` | Abstract base record + declarative metadata contract |
| `Services/Notifications/Events/ShiftAssignedEvent.cs` | First concrete event (proves REQUEST/actionable/batchable path) |
| `Services/Notifications/Events/ShiftRemovedEvent.cs` | Second concrete event (proves CANCEL path) |
| `Services/Notifications/INotificationDispatcher.cs` | Dispatcher interface (`RaiseAsync`) |
| `Services/Notifications/NotificationDispatcher.cs` | Delegating implementation (switch → existing INotificationService) |
| `Program.cs` (~line 277) | DI registration (1 line) |
| `ShiftManager.Tests/UnitTests/Services/NotificationDispatcherTests.cs` | Unit tests (Moq verify delegation + fail-safe on unmapped) |

---

## Task 1: Metadata enums

**Files:**
- Create: `Services/Notifications/NotificationCategory.cs`
- Create: `Services/Notifications/IcsMethod.cs`

- [ ] **Step 1: Create the category enum**

`Services/Notifications/NotificationCategory.cs`:

```csharp
namespace ShiftManager.Services.Notifications;

/// <summary>
/// Grouping used for per-category user mutes (Phase 2) and digest sectioning.
/// Append new values at the end — values may be persisted in NotificationCategoryMute.
/// </summary>
public enum NotificationCategory
{
    ShiftAssignment = 0,
    ShiftChange = 1,
    Chore = 2,
    OnDuty = 3,
    Swap = 4,
    TimeOff = 5,
    Trainee = 6,
    Account = 7,
    AccountSecurity = 8,
    AccessRequest = 9,
    CalendarEntry = 10,
    Social = 11,
    Feedback = 12,
    System = 13
}
```

- [ ] **Step 2: Create the ICS method enum**

`Services/Notifications/IcsMethod.cs`:

```csharp
namespace ShiftManager.Services.Notifications;

/// <summary>
/// iCalendar METHOD for a calendar-eligible event. Consumed by the ICS builder in Phase 4–5.
/// </summary>
public enum IcsMethod
{
    Request,  // create / invite
    Update,   // modify existing (same UID, SEQUENCE++)
    Cancel    // remove
}
```

- [ ] **Step 3: Commit**

```bash
git add Services/Notifications/NotificationCategory.cs Services/Notifications/IcsMethod.cs
git commit -m "feat(notifications): add NotificationCategory and IcsMethod enums (Phase 0)"
```

---

## Task 2: NotificationEvent base record

**Files:**
- Create: `Services/Notifications/NotificationEvent.cs`

- [ ] **Step 1: Create the abstract base record**

`Services/Notifications/NotificationEvent.cs`:

```csharp
namespace ShiftManager.Services.Notifications;

/// <summary>
/// Base type for every notifiable domain event. Concrete events carry their payload
/// (recipient(s) + render data) and override the metadata that drives the dispatcher.
///
/// In Phase 0 the dispatcher only reads the concrete type (delegating to the existing
/// INotificationService creators). The metadata properties below are the stable contract
/// consumed later: PersonallyActionable + SecurityCritical drive Quiet-mode email rules
/// (Phase 2); CalendarEligible + Ics drive .ics generation (Phase 4–5); Batchable drives
/// bulk-op coalescing (Phase 3).
/// </summary>
public abstract record NotificationEvent
{
    /// <summary>Category for per-user mutes and digest sectioning.</summary>
    public abstract NotificationCategory Category { get; }

    /// <summary>True if this event still emails when the recipient is in Quiet mode.</summary>
    public abstract bool PersonallyActionable { get; }

    /// <summary>True if this event ALWAYS emails — ignores Quiet mode and category mutes.</summary>
    public virtual bool SecurityCritical => false;

    /// <summary>True if a calendar (.ics) artifact should accompany this event.</summary>
    public virtual bool CalendarEligible => false;

    /// <summary>iCalendar METHOD when <see cref="CalendarEligible"/>; otherwise null.</summary>
    public virtual IcsMethod? Ics => null;

    /// <summary>True if this event can be coalesced into one summary email during bulk ops.</summary>
    public virtual bool Batchable => false;
}
```

- [ ] **Step 2: Commit**

```bash
git add Services/Notifications/NotificationEvent.cs
git commit -m "feat(notifications): add NotificationEvent base record with metadata contract (Phase 0)"
```

---

## Task 3: First concrete events

**Files:**
- Create: `Services/Notifications/Events/ShiftAssignedEvent.cs`
- Create: `Services/Notifications/Events/ShiftRemovedEvent.cs`

- [ ] **Step 1: Create ShiftAssignedEvent**

`Services/Notifications/Events/ShiftAssignedEvent.cs`:

```csharp
namespace ShiftManager.Services.Notifications.Events;

/// <summary>
/// Raised when a user is assigned to a shift. Maps to
/// INotificationService.CreateShiftAddedNotificationAsync (in-app + email today).
/// </summary>
public sealed record ShiftAssignedEvent : NotificationEvent
{
    public required int RecipientUserId { get; init; }
    public required string ShiftTypeName { get; init; }
    public required DateOnly ShiftDate { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }

    public override NotificationCategory Category => NotificationCategory.ShiftAssignment;
    public override bool PersonallyActionable => true;
    public override bool CalendarEligible => true;
    public override IcsMethod? Ics => IcsMethod.Request;
    public override bool Batchable => true;
}
```

- [ ] **Step 2: Create ShiftRemovedEvent**

`Services/Notifications/Events/ShiftRemovedEvent.cs`:

```csharp
namespace ShiftManager.Services.Notifications.Events;

/// <summary>
/// Raised when a user is unassigned from a shift. Maps to
/// INotificationService.CreateShiftRemovedNotificationAsync (in-app + email today).
/// </summary>
public sealed record ShiftRemovedEvent : NotificationEvent
{
    public required int RecipientUserId { get; init; }
    public required string ShiftTypeName { get; init; }
    public required DateOnly ShiftDate { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }

    public override NotificationCategory Category => NotificationCategory.ShiftAssignment;
    public override bool PersonallyActionable => true;
    public override bool CalendarEligible => true;
    public override IcsMethod? Ics => IcsMethod.Cancel;
    public override bool Batchable => true;
}
```

- [ ] **Step 3: Commit**

```bash
git add Services/Notifications/Events/ShiftAssignedEvent.cs Services/Notifications/Events/ShiftRemovedEvent.cs
git commit -m "feat(notifications): add ShiftAssigned/ShiftRemoved events (Phase 0)"
```

---

## Task 4: Dispatcher interface

**Files:**
- Create: `Services/Notifications/INotificationDispatcher.cs`

- [ ] **Step 1: Create the interface**

`Services/Notifications/INotificationDispatcher.cs`:

```csharp
using System.Threading.Tasks;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// Single entry point for raising notification events. Implementations fan an event out
/// to the in-app and email channels (and later calendar). Fire-and-forget by contract:
/// a notification failure must never throw into the calling business action.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Raise a notification event. In Phase 0 this delegates to the existing
    /// INotificationService creators. Unmapped event types are logged and skipped
    /// (never thrown) so a missing mapping cannot break a business action.
    /// </summary>
    Task RaiseAsync(NotificationEvent evt);
}
```

- [ ] **Step 2: Commit**

```bash
git add Services/Notifications/INotificationDispatcher.cs
git commit -m "feat(notifications): add INotificationDispatcher interface (Phase 0)"
```

---

## Task 5: Dispatcher implementation (delegating) — TDD

**Files:**
- Create: `Services/Notifications/NotificationDispatcher.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/NotificationDispatcherTests.cs`

- [ ] **Step 1: Write the failing tests**

`ShiftManager.Tests/UnitTests/Services/NotificationDispatcherTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Services;
using ShiftManager.Services.Notifications;
using ShiftManager.Services.Notifications.Events;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// ND-01: ShiftAssignedEvent delegates to CreateShiftAddedNotificationAsync with exact args.
/// ND-02: ShiftRemovedEvent delegates to CreateShiftRemovedNotificationAsync with exact args.
/// ND-03: An unmapped event type does NOT throw and calls no INotificationService method.
/// </summary>
public class NotificationDispatcherTests
{
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ILogger<NotificationDispatcher>> _logger = new();

    private NotificationDispatcher CreateSut() => new(_notifications.Object, _logger.Object);

    [Fact] // ND-01
    public async Task RaiseAsync_ShiftAssignedEvent_DelegatesToCreateShiftAdded()
    {
        var sut = CreateSut();
        var evt = new ShiftAssignedEvent
        {
            RecipientUserId = 42,
            ShiftTypeName = "Morning",
            ShiftDate = new DateOnly(2026, 6, 11),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0)
        };

        await sut.RaiseAsync(evt);

        _notifications.Verify(n => n.CreateShiftAddedNotificationAsync(
            42, "Morning", new DateOnly(2026, 6, 11), new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Times.Once);
    }

    [Fact] // ND-02
    public async Task RaiseAsync_ShiftRemovedEvent_DelegatesToCreateShiftRemoved()
    {
        var sut = CreateSut();
        var evt = new ShiftRemovedEvent
        {
            RecipientUserId = 7,
            ShiftTypeName = "Night",
            ShiftDate = new DateOnly(2026, 6, 12),
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0)
        };

        await sut.RaiseAsync(evt);

        _notifications.Verify(n => n.CreateShiftRemovedNotificationAsync(
            7, "Night", new DateOnly(2026, 6, 12), new TimeOnly(22, 0), new TimeOnly(6, 0)),
            Times.Once);
    }

    [Fact] // ND-03
    public async Task RaiseAsync_UnmappedEvent_DoesNotThrow_AndCallsNothing()
    {
        var sut = CreateSut();
        var act = async () => await sut.RaiseAsync(new UnmappedTestEvent());

        await act.Should().NotThrowAsync();
        _notifications.VerifyNoOtherCalls();
    }

    private sealed record UnmappedTestEvent : NotificationEvent
    {
        public override NotificationCategory Category => NotificationCategory.System;
        public override bool PersonallyActionable => false;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~NotificationDispatcherTests"`
Expected: FAIL — `NotificationDispatcher` does not exist (compile error).

- [ ] **Step 3: Write the dispatcher implementation**

`Services/Notifications/NotificationDispatcher.cs`:

```csharp
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShiftManager.Services.Notifications.Events;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// Phase 0 dispatcher: delegates each event to the existing INotificationService creator
/// (which already persists in-app + sends email). No metadata is consumed yet — that
/// arrives in Phase 2. Unmapped events are logged and skipped, never thrown.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationService _notifications;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(INotificationService notifications, ILogger<NotificationDispatcher> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public async Task RaiseAsync(NotificationEvent evt)
    {
        switch (evt)
        {
            case ShiftAssignedEvent e:
                await _notifications.CreateShiftAddedNotificationAsync(
                    e.RecipientUserId, e.ShiftTypeName, e.ShiftDate, e.StartTime, e.EndTime);
                break;

            case ShiftRemovedEvent e:
                await _notifications.CreateShiftRemovedNotificationAsync(
                    e.RecipientUserId, e.ShiftTypeName, e.ShiftDate, e.StartTime, e.EndTime);
                break;

            default:
                _logger.LogWarning(
                    "No dispatcher mapping for notification event {EventType}; skipped",
                    evt.GetType().Name);
                break;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~NotificationDispatcherTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Services/Notifications/NotificationDispatcher.cs ShiftManager.Tests/UnitTests/Services/NotificationDispatcherTests.cs
git commit -m "feat(notifications): add delegating NotificationDispatcher + tests (Phase 0)"
```

---

## Task 6: Register the dispatcher in DI

**Files:**
- Modify: `Program.cs` (immediately after the `INotificationService` registration, ~line 277)

- [ ] **Step 1: Add the registration**

Find this line in `Program.cs` (~277):

```csharp
builder.Services.AddScoped<INotificationService, NotificationService>();
```

Add directly beneath it:

```csharp
builder.Services.AddScoped<ShiftManager.Services.Notifications.INotificationDispatcher, ShiftManager.Services.Notifications.NotificationDispatcher>();
```

- [ ] **Step 2: Build to verify DI compiles**

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded, 0 errors.
> If the build reports the executable is locked / in use, STOP — another session or a running app holds it. Resolve per the executable-lock policy (ask the user to close it, or terminate the verified process) before retrying. Do NOT rebuild against a locked binary.

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "chore(notifications): register INotificationDispatcher in DI (Phase 0)"
```

---

## Task 7: Full suite regression check

- [ ] **Step 1: Run the full unit suite sequentially**

Run: `dotnet test ShiftManager.Tests -- xUnit.ParallelizeTestCollections=false`
Expected: All tests pass (Phase 0 is additive; no existing behavior changed). The sequential flag avoids the known spurious `:memory:` SQLite contention failures under parallel runs.

- [ ] **Step 2: Confirm no production call site changed**

Run: `git log --oneline -7`
Expected: only the Phase 0 commits above; no edits to `Pages/`, `Controllers/`, or existing `Services/*.cs` other than the 1-line `Program.cs` DI registration.

---

## Self-Review

**Spec coverage (§5.1–5.4):**
- §5.1 dispatch flow → `INotificationDispatcher.RaiseAsync` (Task 4–5). ✓ (in-app+email fan-out delegated to existing service; calendar/batching arrive in later phases as planned)
- §5.2 event metadata → `NotificationEvent` base (Task 2): Category, PersonallyActionable, SecurityCritical, CalendarEligible+Ics, Batchable. ✓
- §5.3 email precedence chain → NOT in Phase 0 by design (consumed in Phase 2; flags are defined now so events don't churn). ✓ (intentional deferral, documented)
- §5.4 event catalogue → only ShiftAssigned/ShiftRemoved in Phase 0 (representative REQUEST + CANCEL); the remaining ~30 events are added during Phase 3 coverage migration. ✓ (intentional — foundation phase)

**Placeholder scan:** none — every step has full code or an exact command. ✓

**Type consistency:** `RaiseAsync(NotificationEvent)` matches across interface, impl, and tests; event property names (`RecipientUserId`, `ShiftTypeName`, `ShiftDate`, `StartTime`, `EndTime`) match the `INotificationService.CreateShiftAddedNotificationAsync` parameter order used in the dispatcher and asserted in ND-01/ND-02. `NotificationCategory.System` and `IcsMethod.Request/Cancel` exist (Task 1) and are used in Tasks 2–3 and the test's `UnmappedTestEvent`. ✓

---

## What Phase 0 deliberately does NOT do (handed to later phases)

- **Consume the metadata** (Quiet mode, security-critical-always-email, category mutes) → **Phase 2**.
- **Generate/attach .ics or the calendar feed** → **Phase 4 (feed)** / **Phase 5 (per-event)**.
- **Bulk batching** → **Phase 3**.
- **Migrate real call sites** off the old dual-call pattern, and add the gap events (trainee email, feedback email, account actions, approver notifications, calendar text entries) → **Phase 3**.
- **Per-user language** → **Phase 1**.

Each subsequent phase will get its own detailed plan written when we reach it.
