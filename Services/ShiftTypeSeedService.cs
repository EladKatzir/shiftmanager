using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class ShiftTypeSeedService : IShiftTypeSeedService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ShiftTypeSeedService> _logger;

    public ShiftTypeSeedService(AppDbContext db, ILogger<ShiftTypeSeedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedForMoleculeAsync(int moleculeId)
    {
        var molecule = await _db.Molecules.FindAsync(moleculeId);
        if (molecule == null)
        {
            _logger.LogWarning("Cannot seed shift types: molecule {MoleculeId} not found", moleculeId);
            return;
        }

        // Check if already seeded (idempotent)
        var hasShifts = await _db.ShiftTypes.AnyAsync(st => st.MoleculeId == moleculeId);
        if (hasShifts)
        {
            _logger.LogDebug("Molecule {MoleculeId} already has shift types, skipping seed", moleculeId);
            return;
        }

        switch (molecule.Type)
        {
            case MoleculeType.Workforce:
                // Seed shared HOME + OFFLINE immediately
                _db.ShiftTypes.AddRange(TechShiftTypeSeed.GetWorkforceSharedShiftTypes(moleculeId));
                await _db.SaveChangesAsync();
                _logger.LogInformation("Seeded HOME+OFFLINE for workforce molecule {MoleculeId}", moleculeId);
                break;

            case MoleculeType.Tech:
                // Seed HOME + OFFLINE immediately (tech shifts are created manually via Blueprints)
                _db.ShiftTypes.AddRange(TechShiftTypeSeed.GetWorkforceSharedShiftTypes(moleculeId));
                await _db.SaveChangesAsync();
                _logger.LogInformation("Seeded HOME+OFFLINE for tech molecule {MoleculeId}", moleculeId);
                break;

            case MoleculeType.Helper:
            case MoleculeType.System:
                _logger.LogDebug("No auto-seed for {Type} molecule {MoleculeId}", molecule.Type, moleculeId);
                break;
        }
    }

    public async Task SeedForJobTypeAsync(int moleculeId, int jobTypeId)
    {
        var molecule = await _db.Molecules.FindAsync(moleculeId);
        if (molecule == null || molecule.Type != MoleculeType.Workforce)
        {
            _logger.LogDebug("Skipping per-JobType seed: molecule {MoleculeId} not found or not workforce", moleculeId);
            return;
        }

        // Check if MORNING already exists for this molecule+jobType (idempotent)
        var hasPerJobTypeShifts = await _db.ShiftTypes
            .AnyAsync(st => st.MoleculeId == moleculeId && st.JobTypeId == jobTypeId && st.Key == ShiftType.KEY_MORNING);
        if (hasPerJobTypeShifts)
        {
            _logger.LogDebug("Molecule {MoleculeId} already has per-JobType shifts for {JobTypeId}", moleculeId, jobTypeId);
            return;
        }

        _db.ShiftTypes.AddRange(TechShiftTypeSeed.GetWorkforceShiftTypes(moleculeId, jobTypeId));
        await _db.SaveChangesAsync();
        _logger.LogInformation("Seeded MORNING/AFTERNOON/NIGHT for molecule {MoleculeId} jobType {JobTypeId}", moleculeId, jobTypeId);
    }
}
