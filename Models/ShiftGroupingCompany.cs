namespace ShiftManager.Models;

public class ShiftGroupingCompany
{
    public int ShiftGroupingId { get; set; }
    public int CompanyId { get; set; }

    public ShiftGrouping ShiftGrouping { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
