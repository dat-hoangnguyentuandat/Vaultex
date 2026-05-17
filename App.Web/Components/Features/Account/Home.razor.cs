using Microsoft.AspNetCore.Components;
using App.Web.Services;

namespace App.Web.Components.Features.Account;

public partial class Home : ComponentBase
{
    [Inject] private I18nService I18n { get; set; } = default!;
    [Inject] private ThemeService Theme { get; set; } = default!;

    protected string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }
}
