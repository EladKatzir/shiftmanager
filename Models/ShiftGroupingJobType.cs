namespace ShiftManager.Models;

public class ShiftGroupingJobType
{
    public int ShiftGroupingId { get; set; }
    public int JobTypeId { get; set; }

    public ShiftGrouping ShiftGrouping { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
}
