namespace App.Domain.Entities
{
    public class TenantConfiguration
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public int AccessTokenLifetimeSeconds { get; set; } = 3600;
        public int RefreshTokenLifetimeDays { get; set; } = 30;
        public int AuthorizationCodeLifetimeMinutes { get; set; } = 5;
        public bool AllowPasswordFlow { get; set; } = false;
        public bool AllowAuthorizationCodeFlow { get; set; } = true;
        public bool AllowClientCredentialsFlow { get; set; } = true;
        public bool AllowDeviceFlow { get; set; } = false;
        public int MaxUsers { get; set; } = 1000;

        public Tenant Tenant { get; set; } = null!;
    }
}
