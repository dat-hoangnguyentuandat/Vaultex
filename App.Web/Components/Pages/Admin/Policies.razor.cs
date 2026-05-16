using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Pages.Admin;

public partial class Policies : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;

    private List<PolicyDto> policies = [];
    private bool loading = true;
    private bool showForm;
    private Guid? editId;
    private PolicyForm form = new();
    private string formError = "";

    protected override async Task OnInitializedAsync() => await LoadPolicies();

    private async Task LoadPolicies()
    {
        loading = true;
        var result = await Api.CallApiAsync<List<PolicyDto>>("api/admin/policies");
        policies = result ?? [];
        loading = false;
    }

    private void OpenCreate() { editId = null; form = new(); formError = ""; showForm = true; }

    private void OpenEdit(PolicyDto p)
    {
        editId = p.Id;
        form = new PolicyForm { Name = p.Name, Resource = p.Resource, Action = p.Action, IsActive = p.IsActive };
        formError = "";
        showForm = true;
    }

    private void CloseForm() { showForm = false; formError = ""; }

    private async Task SavePolicy()
    {
        formError = "";
        var body = new
        {
            form.Name, form.Resource, form.Action,
            ConditionsJson = string.IsNullOrWhiteSpace(form.ConditionsJson) ? "[]" : form.ConditionsJson,
            form.IsActive
        };
        var resp = editId.HasValue
            ? await Api.PutAsync($"api/admin/policies/{editId}", body)
            : await Api.PostAsync("api/admin/policies", body);
        if (resp.IsSuccessStatusCode) { CloseForm(); await LoadPolicies(); }
        else formError = await resp.Content.ReadAsStringAsync();
    }

    private async Task DeletePolicy(Guid id)
    {
        await Api.DeleteAsync($"api/admin/policies/{id}");
        await LoadPolicies();
    }

    private record PolicyDto(Guid Id, string Name, string Resource, string Action, bool IsActive);

    private class PolicyForm
    {
        public string Name { get; set; } = "";
        public string Resource { get; set; } = "";
        public string Action { get; set; } = "";
        public string ConditionsJson { get; set; } = "[]";
        public bool IsActive { get; set; } = true;
    }
}
