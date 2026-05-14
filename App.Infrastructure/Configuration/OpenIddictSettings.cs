namespace App.Infrastructure.Configuration
{
    public class OpenIddictSettings
    {
        public TokenLifetimesSettings TokenLifetimes { get; set; } = new();
        public List<ScopeSettings> Scopes { get; set; } = new();
        public Dictionary<string, ApplicationSettings> Applications { get; set; } = new();
    }

    public class TokenLifetimesSettings
    {
        public int AccessTokenSeconds { get; set; } = 3600;
        public int RefreshTokenDays { get; set; } = 30;
        public int AuthorizationCodeMinutes { get; set; } = 5;
    }

    public class ScopeSettings
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class ApplicationSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public List<string> RedirectUris { get; set; } = new();
        public List<string> PostLogoutRedirectUris { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public List<string> Requirements { get; set; } = new();
    }
}
