using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Pages.Admin;

public partial class Dashboard : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;

    private string tenantName = "Loading...";
    private int userCount, roleCount, policyCount, auditCount;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var tenantResp = await Api.CallApiAsync(HttpMethod.Get, "api/admin/tenant");
            if (tenantResp.IsSuccessStatusCode)
            {
                var json = await tenantResp.Content.ReadAsStringAsync();
                var tenant = System.Text.Json.JsonSerializer.Deserialize<TenantDto>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                tenantName = tenant?.Name ?? "Platform";
            }
            else
            {
                tenantName = "Platform";
            }
        }
        catch { tenantName = "Platform"; }

        try
        {
            var users = await Api.CallApiAsync<PagedResult<UserSummary>>("api/admin/users?pageSize=1");
            userCount = users?.Total ?? 0;
        }
        catch { }

        try
        {
            var roles = await Api.CallApiAsync<List<object>>("api/admin/roles");
            roleCount = roles?.Count ?? 0;
        }
        catch { }

        try
        {
            var policies = await Api.CallApiAsync<List<object>>("api/admin/policies");
            policyCount = policies?.Count ?? 0;
        }
        catch { }

        try
        {
            var audit = await Api.CallApiAsync<PagedResult<object>>("api/audit?pageSize=1");
            auditCount = audit?.Total ?? 0;
        }
        catch { }
    }

    private record PagedResult<T>(int Total, int Page, int PageSize, List<T> Items);
    private record UserSummary(Guid Id, string Email, string? FullName, bool IsActive);
    private record TenantDto(Guid Id, string Name, string Subdomain, string Region, string Status);
}
