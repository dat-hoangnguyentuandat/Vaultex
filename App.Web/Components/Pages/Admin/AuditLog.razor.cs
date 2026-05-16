using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Pages.Admin;

public partial class AuditLog : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;

    private List<AuditDto> logs = [];
    private AuditDto? selected;
    private bool loading = true;
    private int page = 1, totalPages = 1, total;
    private const int PageSize = 50;
    private string filterEvent = "";
    private DateTime? filterFrom, filterTo;

    protected override async Task OnInitializedAsync() => await LoadLogs();

    private async Task LoadLogs()
    {
        loading = true;
        var url = $"api/audit?page={page}&pageSize={PageSize}";
        if (!string.IsNullOrEmpty(filterEvent)) url += $"&eventType={Uri.EscapeDataString(filterEvent)}";
        if (filterFrom.HasValue) url += $"&dateFrom={filterFrom.Value:yyyy-MM-dd}";
        if (filterTo.HasValue) url += $"&dateTo={filterTo.Value:yyyy-MM-dd}";
        var result = await Api.CallApiAsync<PagedResult<AuditDto>>(url);
        logs = result?.Items ?? [];
        total = result?.Total ?? 0;
        totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)PageSize);
        loading = false;
    }

    private async Task PrevPage() { page--; await LoadLogs(); }
    private async Task NextPage() { page++; await LoadLogs(); }

    protected static string AuditBadgeClass(string eventType) => eventType switch
    {
        "LOGIN_SUCCESS" or "EMAIL_CONFIRMED" => "abadge--green",
        "LOGIN_FAILED" or "USER_DELETED" => "abadge--red",
        "LOGOUT" => "abadge--gray",
        "ACCESS_DENIED" or "PASSWORD_RESET" or "PASSWORD_CHANGED" or "USER_STATUS_CHANGED" or "TENANT_STATUS_CHANGED" => "abadge--yellow",
        "REGISTER" or "USER_CREATED" or "TENANT_CREATED" => "abadge--blue",
        _ => "abadge--gray"
    };

    private record PagedResult<T>(int Total, int Page, int PageSize, List<T> Items);
    private record AuditDto(Guid Id, Guid? TenantId, Guid? UserId, string EventType,
        string? ResourceType, string? ResourceId, string? IpAddress, DateTime Timestamp,
        string? OldValue, string? NewValue);
}
