using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Pages.Admin;

public partial class Users : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;

    private List<UserDto> users = [];
    private List<string> availableRoles = [];
    private bool loading = true;
    private string search = "";
    private int page = 1, totalPages = 1;
    private const int PageSize = 20;

    private bool showCreate;
    private string newEmail = "", newFullName = "", newPassword = "", newRole = "", createError = "";

    private UserDto? rolesUser;
    private HashSet<string> selectedRoles = [];
    private UserDto? deleteUser;

    protected override async Task OnInitializedAsync()
    {
        await LoadRoles();
        await LoadUsers();
    }

    private async Task LoadUsers()
    {
        loading = true;
        var result = await Api.CallApiAsync<PagedResult<UserDto>>(
            $"api/admin/users?search={Uri.EscapeDataString(search)}&page={page}&pageSize={PageSize}");
        users = result?.Items ?? [];
        totalPages = result is null ? 1 : (int)Math.Ceiling(result.Total / (double)PageSize);
        loading = false;
    }

    private async Task LoadRoles()
    {
        var roles = await Api.CallApiAsync<List<RoleDto>>("api/admin/roles");
        availableRoles = roles?.Select(r => r.ShortName ?? "").Where(r => r != "").ToList() ?? [];
    }

    private async Task ToggleActive(UserDto u)
    {
        await Api.PutAsync($"api/admin/users/{u.Id}/activate?active={!u.IsActive}", new { });
        await LoadUsers();
    }

    private async Task OpenRoles(UserDto u)
    {
        rolesUser = u;
        var detail = await Api.CallApiAsync<UserDetail>($"api/admin/users/{u.Id}");
        selectedRoles = detail?.Roles?.ToHashSet() ?? [];
    }

    private void ToggleRole(string role, bool add)
    {
        if (add) selectedRoles.Add(role);
        else selectedRoles.Remove(role);
    }

    private async Task SaveRoles()
    {
        if (rolesUser is null) return;
        await Api.PutAsync($"api/admin/users/{rolesUser.Id}/roles", new { Roles = selectedRoles.ToList() });
        rolesUser = null;
    }

    private async Task CreateUser()
    {
        createError = "";
        var resp = await Api.PostAsync("api/admin/users", new
        {
            Email = newEmail, Password = newPassword,
            FullName = newFullName, Role = newRole
        });
        if (resp.IsSuccessStatusCode)
        {
            showCreate = false;
            newEmail = newFullName = newPassword = newRole = "";
            await LoadUsers();
        }
        else
        {
            createError = await resp.Content.ReadAsStringAsync();
        }
    }

    private async Task PrevPage() { page--; await LoadUsers(); }
    private async Task NextPage() { page++; await LoadUsers(); }
    private void ConfirmDelete(UserDto u) => deleteUser = u;

    private async Task DeleteUser()
    {
        if (deleteUser is null) return;
        await Api.DeleteAsync($"api/admin/users/{deleteUser.Id}");
        deleteUser = null;
        await LoadUsers();
    }

    protected static string GetInitials(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }

    private record PagedResult<T>(int Total, int Page, int PageSize, List<T> Items);
    private record UserDto(Guid Id, string Email, string? FullName, bool IsActive, DateTime? LastLoginAt);
    private record UserDetail(Guid Id, string Email, string? FullName, bool IsActive, List<string> Roles);
    private record RoleDto(Guid Id, string? ShortName);
}
