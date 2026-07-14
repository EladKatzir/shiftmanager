using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Persistence;

/// <summary>
/// Guards the ShiftAssignment.Trainee navigation mapping. The real FK column is TraineeUserId
/// (not the EF-conventional TraineeId), so without an explicit Fluent mapping EF binds the Trainee
/// navigation to an auto-created shadow FK "TraineeId" that the code never writes — meaning
/// Include(sa => sa.Trainee) always returned null and the trainee's name never rendered on the
/// calendar. This is a model-config concern (identical on any provider), so the In-Memory provider
/// is appropriate; it is NOT a LINQ-to-SQL translation test.
/// </summary>
public sealed class ShiftAssignmentTraineeNavigationTests
{
    [Fact]
    public async Task IncludeTrainee_LoadsTraineeFromTraineeUserId()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nameof(IncludeTrainee_LoadsTraineeFromTraineeUserId))
            .Options;

        int assignmentId;
        var empty = System.Array.Empty<byte>();

        // Seed a worker + a trainee + an assignment that shadows the trainee onto the worker's shift.
        await using (var db = new AppDbContext(options))
        {
            var worker = new AppUser { CompanyId = 1, DisplayName = "Worker X", Email = "worker@test", PasswordHash = empty, PasswordSalt = empty, IsActive = true, Role = UserRole.Employee };
            var trainee = new AppUser { CompanyId = 1, DisplayName = "Trainee Y", Email = "trainee@test", PasswordHash = empty, PasswordSalt = empty, IsActive = true, Role = UserRole.Trainee };
            db.Users.AddRange(worker, trainee);
            await db.SaveChangesAsync();

            var instance = new ShiftInstance { ShiftTypeId = 1, CompanyId = 1, WorkDate = new DateOnly(2026, 7, 14), StaffingRequired = 1 };
            db.ShiftInstances.Add(instance);
            await db.SaveChangesAsync();

            var assignment = new ShiftAssignment
            {
                CompanyId = 1,
                ShiftInstanceId = instance.Id,
                UserId = worker.Id,
                TraineeUserId = trainee.Id   // the column the code actually writes
            };
            db.ShiftAssignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        // Fresh context so the Trainee navigation is resolved from the store via its mapped FK.
        await using (var db = new AppDbContext(options))
        {
            var assignment = await db.ShiftAssignments
                .Include(a => a.Trainee)
                .FirstAsync(a => a.Id == assignmentId);

            assignment.TraineeUserId.Should().Be(
                (await db.Users.FirstAsync(u => u.DisplayName == "Trainee Y")).Id);
            assignment.Trainee.Should().NotBeNull(
                "the Trainee navigation must be mapped to TraineeUserId so Include(Trainee) resolves it");
            assignment.Trainee!.DisplayName.Should().Be("Trainee Y");
        }
    }
}
