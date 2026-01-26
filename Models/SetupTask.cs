using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class SetupTask
{
    public int Id { get; set; }
    public SetupTaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Context
    public int? MoleculeId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }

    // Suggested action
    public int? SuggestedUserId { get; set; }
    public string? SuggestionReason { get; set; }

    // Assignment
    public int AssignedToUserId { get; set; }
    public SetupTaskStatus Status { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }

    // Navigation
    public Molecule? Molecule { get; set; }
    public Company? Company { get; set; }
    public JobType? JobType { get; set; }
    public AppUser? SuggestedUser { get; set; }
    public AppUser AssignedToUser { get; set; } = null!;
    public AppUser? CompletedByUser { get; set; }
}
