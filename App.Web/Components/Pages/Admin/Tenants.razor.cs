using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Pages.Admin;

public partial class Tenants : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;

    private List<TenantDto> tenants = [];
    private bool loading = true;
    private string search = "";
    private int currentPage = 1, totalPages = 1;
    private const int PageSize = 20;

    private bool showCreate;
    private string newName = "", newSubdomain = "", newRegion = "", createError = "";

    private TenantDto? editTenant;
    private string editName = "", editRegion = "";

    private TenantDto? adminTenant;
    private string adminEmail = "", adminFullName = "", adminPassword = "", adminError = "";

    protected override async Task OnInitializedAsync() => await LoadTenants();

    private async Task LoadTenants()
    {
        loading = true;
        var result = await Api.CallApiAsync<PagedResult<TenantDto>>(
            $"api/platform/tenants?search={Uri.EscapeDataString(search)}&page={currentPage}&pageSize={PageSize}");
        tenants = result?.Items ?? [];
        totalPages = result is null ? 1 : (int)Math.Ceiling(result.Total / (double)PageSize);
        loading = false;
    }

    private async Task CreateTenant()
    {
        createError = "";
        var resp = await Api.PostAsync("api/platform/tenants", new { Name = newName, Subdomain = newSubdomain, Region = newRegion });
        if (resp.IsSuccessStatusCode) { showCreate = false; newName = newSubdomain = newRegion = ""; await LoadTenants(); }
        else createError = await resp.Content.ReadAsStringAsync();
    }

    private void OpenEdit(TenantDto t) { editTenant = t; editName = t.Name; editRegion = t.Region; }

    private async Task SaveEdit()
    {
        if (editTenant is null) return;
        await Api.PutAsync($"api/platform/tenants/{editTenant.Id}", new { Name = editName, Region = editRegion });
        editTenant = null;
        await LoadTenants();
    }

    private async Task SetStatus(TenantDto t, string status)
    {
        await Api.PutAsync($"api/platform/tenants/{t.Id}/status?status={status}", new { });
        await LoadTenants();
    }

    private Task SuspendTenant(TenantDto t) => SetStatus(t, "Suspended");
    private Task ActivateTenant(TenantDto t) => SetStatus(t, "Active");

    private void OpenCreateAdmin(TenantDto t) { adminTenant = t; adminEmail = adminFullName = adminPassword = adminError = ""; }

    private async Task CreateAdmin()
    {
        adminError = "";
        var resp = await Api.PostAsync($"api/platform/tenants/{adminTenant!.Id}/admin", new
        {
            Email = adminEmail, Password = adminPassword, FullName = adminFullName
        });
        if (resp.IsSuccessStatusCode) adminTenant = null;
        else adminError = await resp.Content.ReadAsStringAsync();
    }

    private async Task PrevPage() { currentPage--; await LoadTenants(); }
    private async Task NextPage() { currentPage++; await LoadTenants(); }

    protected static string TenantBadgeClass(string status) => status switch
    {
        "Active" => "abadge--green",
        "Suspended" => "abadge--yellow",
        "Deleted" => "abadge--red",
        _ => "abadge--gray"
    };

    private record PagedResult<T>(int Total, int Page, int PageSize, List<T> Items);
    private record TenantDto(Guid Id, string Name, string Subdomain, string Region, string Status, DateTime CreatedAt, int? UserCount);
}
