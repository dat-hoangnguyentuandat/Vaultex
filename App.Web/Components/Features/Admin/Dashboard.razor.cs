using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using App.Web.Services;

namespace App.Web.Components.Features.Admin;

public partial class Dashboard : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    private bool isPlatformAdmin;

    // Tenant Admin stats
    private string tenantName = "Loading...";
    private int userCount, roleCount, policyCount, auditCount;

    // PlatformAdmin stats
    private int tenantCount, activeTenants, totalUsers, auditToday;

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthState;
        isPlatformAdmin = auth.User.IsInRole("PlatformAdmin");

        if (isPlatformAdmin)
            await LoadPlatformStats();
        else
            await LoadTenantStats();
    }

    private async Task LoadPlatformStats()
    {
        try
        {
            var stats = await Api.CallApiAsync<PlatformStats>("api/platform/stats");
            if (stats is not null)
            {
                tenantCount = stats.TenantCount;
                activeTenants = stats.ActiveTenants;
                totalUsers = stats.UserCount;
                auditToday = stats.AuditToday;
            }
        }
        catch { }
    }

    private async Task LoadTenantStats()
    {
        try
        {
            var tenantResp = await Api.CallApiAsync(HttpMethod.Get, "api/admin/tenant");
            if (tenantResp.IsSuccessStatusCode)
            {
                var json = await tenantResp.Content.ReadAsStringAsync();
                var tenant = System.Text.Json.JsonSerializer.Deserialize<TenantDto>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                tenantName = tenant?.Name ?? "—";
            }
        }
        catch { tenantName = "—"; }

        try { var r = await Api.CallApiAsync<PagedResult<object>>("api/admin/users?pageSize=1"); userCount = r?.Total ?? 0; } catch { }
        try { var r = await Api.CallApiAsync<List<object>>("api/admin/roles"); roleCount = r?.Count ?? 0; } catch { }
        try { var r = await Api.CallApiAsync<List<object>>("api/admin/policies"); policyCount = r?.Count ?? 0; } catch { }
        try { var r = await Api.CallApiAsync<PagedResult<object>>("api/audit?pageSize=1"); auditCount = r?.Total ?? 0; } catch { }
    }

    private record PagedResult<T>(int Total, int Page, int PageSize, List<T> Items);
    private record TenantDto(Guid Id, string Name, string Subdomain, string Region, string Status);
    private record PlatformStats(int TenantCount, int ActiveTenants, int UserCount, int AuditToday);
}
