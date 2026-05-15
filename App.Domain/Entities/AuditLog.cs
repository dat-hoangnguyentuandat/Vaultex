namespace App.Domain.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string EventType { get; set; } = "";
        public string? ResourceType { get; set; }
        public string? ResourceId { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public static class AuditEventTypes
    {
        public const string LoginSuccess    = "LOGIN_SUCCESS";
        public const string LoginFailed     = "LOGIN_FAILED";
        public const string Logout          = "LOGOUT";
        public const string TokenIssued     = "TOKEN_ISSUED";
        public const string TokenRevoked    = "TOKEN_REVOKED";
        public const string AccessDenied    = "ACCESS_DENIED";
        public const string Register        = "REGISTER";
        public const string PasswordReset   = "PASSWORD_RESET";
        public const string UserCreated     = "USER_CREATED";
        public const string UserDeleted     = "USER_DELETED";
        public const string RoleChanged     = "ROLE_CHANGED";
        public const string PolicyChanged   = "POLICY_CHANGED";
        public const string TenantCreated       = "TENANT_CREATED";
        public const string TenantUpdated       = "TENANT_UPDATED";
        public const string TenantStatusChanged = "TENANT_STATUS_CHANGED";
        public const string UserStatusChanged   = "USER_STATUS_CHANGED";
        public const string EmailConfirmed  = "EMAIL_CONFIRMED";
        public const string PasswordChanged = "PASSWORD_CHANGED";
        public const string ProfileUpdated  = "PROFILE_UPDATED";
        public const string ProductCreated  = "PRODUCT_CREATED";
        public const string ProductUpdated  = "PRODUCT_UPDATED";
        public const string ProductDeleted  = "PRODUCT_DELETED";
    }
}
