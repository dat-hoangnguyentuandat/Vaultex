namespace App.Domain.Constants
{
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Employee = "Employee";
        public const string Viewer = "Viewer";
        public const string PlatformAdmin = "PlatformAdmin";

        public static readonly string[] All = { Admin, Manager, Employee, Viewer };
    }

    public static class Permissions
    {
        // Users
        public const string UserRead = "user:read";
        public const string UserManage = "user:manage";

        // Admin
        public const string AdminAll = "admin:*";

        // Platform
        public const string PlatformAdmin = "platform:admin";
    }

    public static class RolePermissions
    {
        public static readonly Dictionary<string, string[]> Mapping = new()
        {
            [Roles.Admin] = new[]
            {
                Permissions.AdminAll,
                Permissions.UserRead,
                Permissions.UserManage,
            },
            [Roles.Manager] = new[]
            {
                Permissions.UserRead,
            },
            [Roles.Employee] = Array.Empty<string>(),
            [Roles.Viewer] = Array.Empty<string>(),
            [Roles.PlatformAdmin] = new[]
            {
                Permissions.PlatformAdmin,
                Permissions.AdminAll,
                Permissions.UserRead,
                Permissions.UserManage,
            },
        };
    }
}
