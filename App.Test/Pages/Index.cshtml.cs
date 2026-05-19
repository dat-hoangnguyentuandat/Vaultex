using Microsoft.AspNetCore.Mvc.RazorPages;

namespace App.Test.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _configuration;

    public IndexModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Authority => _configuration["Oidc:Authority"] ?? "https://localhost:7108";
    public string ClientId => _configuration["Oidc:ClientId"] ?? "app-test";
}
