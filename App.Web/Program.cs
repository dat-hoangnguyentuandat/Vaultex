using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})

.AddCookie()

.AddOpenIdConnect(options =>
{
    options.Authority = builder.Configuration["Oidc:Authority"] ?? "https://localhost:7108";
    options.ClientId = builder.Configuration["Oidc:ClientId"] ?? "app-web";
    options.ClientSecret = builder.Configuration["Oidc:ClientSecret"] ?? "app-web-secret";
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.MapInboundClaims = false;
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "role";

    // In Docker the browser-facing authority differs from the internal backchannel URL.
    // Set MetadataAddress to the internal service URL when running in a container.
    var metadataAddress = builder.Configuration["Oidc:MetadataAddress"];
    if (!string.IsNullOrEmpty(metadataAddress))
        options.MetadataAddress = metadataAddress;

    options.Scope.Add("email");
    options.Scope.Add("profile");
    options.Scope.Add("offline_access");
    options.Scope.Add("roles");

    options.CallbackPath = "/signin-oidc";
});

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<App.Web.Services.ApiClient>();
builder.Services.AddHealthChecks();

builder.Services.AddHttpClient("api", client =>
    client.BaseAddress = new Uri(
        builder.Configuration["ApiClient:BaseUrl"] ?? "https://localhost:7108"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("api/auth/login", (string? returnUrl) =>
    Results.Challenge(
        properties: new AuthenticationProperties
        {
            RedirectUri = !string.IsNullOrEmpty(returnUrl) ? returnUrl : "/"
        },
        authenticationSchemes: [OpenIdConnectDefaults.AuthenticationScheme]
    ));

app.MapGet("api/auth/logout", () =>
    Results.SignOut(
        properties: new AuthenticationProperties { RedirectUri = "/" },
        authenticationSchemes: [
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme
        ]
    ));

app.MapRazorComponents<App.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapHealthChecks("/health");

app.Run();
