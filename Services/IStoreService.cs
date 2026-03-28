using ShiftManager.Models;

namespace ShiftManager.Services;

public enum StoreStatusType { Open, Break, Closed, NoHoursSet }

public class StoreStatus
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public StoreStatusType Status { get; set; }
    public string Message { get; set; } = "";  // e.g., "Open until 14:00"
    public TimeOnly? NextChange { get; set; }   // when status changes next
}

public interface IStoreService
{
    Task<List<Store>> GetStoresForAreaAsync(int areaId);
    Task<Store?> GetStoreByIdAsync(int storeId);
    Task<(bool Success, string Message, Store? Store)> CreateStoreAsync(int areaId, string nameEn, string? nameHe, int sortOrder = 0);
    Task<(bool Success, string Message)> UpdateStoreAsync(int storeId, string nameEn, string? nameHe, bool isActive, int sortOrder);
    Task<(bool Success, string Message)> DeleteStoreAsync(int storeId);
    Task<List<StoreHoursEntry>> GetStoreHoursAsync(int storeId);
    Task<(bool Success, string Message)> SetStoreHoursAsync(int storeId, List<StoreHoursEntry> entries);
    Task<StoreStatus?> ComputeStoreStatusAsync(int storeId, DateTime now);
    Task<List<StoreStatus>> ComputeAllStoreStatusesAsync(List<int> storeIds, DateTime now);
}
