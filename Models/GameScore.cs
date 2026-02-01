using System;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// ✅ PHASE 19: Represents a game score entry for the Shift Swap Easter egg game.
/// Each score is associated with a user and company for leaderboard tracking.
/// </summary>
public class GameScore : IBelongsToCompany
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The company this score belongs to (for tenant scoping)
    /// </summary>
    [Required]
    public int CompanyId { get; set; }

    /// <summary>
    /// The user who achieved this score
    /// </summary>
    [Required]
    public int UserId { get; set; }

    /// <summary>
    /// The score achieved in this game session
    /// </summary>
    [Required]
    public int Score { get; set; }

    /// <summary>
    /// When this score was achieved
    /// </summary>
    [Required]
    public DateTime PlayedAt { get; set; }

    /// <summary>
    /// Month/Year identifier for monthly leaderboard filtering (format: "YYYY-MM")
    /// </summary>
    [Required]
    [MaxLength(7)]
    public string CurrentMonth { get; set; } = string.Empty;

    // Navigation properties
    public AppUser? User { get; set; }
    public Company? Company { get; set; }
}
