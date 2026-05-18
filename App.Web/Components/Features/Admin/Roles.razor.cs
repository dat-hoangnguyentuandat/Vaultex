using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Features.Admin;

public partial class Roles : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;

    private List<RoleDto> roles = [];
    private bool loading = true;
    private bool showCreate;
    private string newRoleName = "", createError = "";

    protected override async Task OnInitializedAsync() => await LoadRoles();

    private async Task LoadRoles()
    {
        loading = true;
        var result = await Api.CallApiAsync<List<RoleDto>>("api/admin/roles");
        roles = result ?? [];
        loading = false;
    }

    private async Task CreateRole()
    {
        createError = "";
        var resp = await Api.PostAsync("api/admin/roles", new { Name = newRoleName });
        if (resp.IsSuccessStatusCode)
        {
            showCreate = false;
            newRoleName = "";
            await LoadRoles();
        }
        else
        {
            createError = await resp.Content.ReadAsStringAsync();
        }
    }

    private async Task DeleteRole(Guid id)
    {
        await Api.DeleteAsync($"api/admin/roles/{id}");
        await LoadRoles();
    }

    private record RoleDto(Guid Id, string? ShortName);
}
