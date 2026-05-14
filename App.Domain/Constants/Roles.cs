using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace App.Domain.Constants
{
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Employee = "Employee";
        public const string Viewer = "Viewer";

        public static readonly string[] All = { Admin, Manager, Employee, Viewer };
    }

    public static class Permissions
    {
        // Products
        public const string ProductCreate = "product:create";
        public const string ProductRead = "product:read";
        public const string ProductUpdate = "product:update";
        public const string ProductDelete = "product:delete";

        // Users
        public const string UserRead = "user:read";
        public const string UserManage = "user:manage";

        // Admin
        public const string AdminAll = "admin:*";
    }

    public static class RolePermissions
    {
        public static readonly Dictionary<string, string[]> Mapping = new()
        {
            [Roles.Admin] = new[]
            {
                Permissions.AdminAll,
                Permissions.ProductCreate,
                Permissions.ProductRead,
                Permissions.ProductUpdate,
                Permissions.ProductDelete,
                Permissions.UserRead,
                Permissions.UserManage,
            },
            [Roles.Manager] = new[]
            {
                Permissions.ProductCreate,
                Permissions.ProductRead,
                Permissions.ProductUpdate,
                Permissions.ProductDelete,
                Permissions.UserRead,
            },
            [Roles.Employee] = new[]
            {
                Permissions.ProductRead,
                Permissions.ProductUpdate,
            },
            [Roles.Viewer] = new[]
            {
                Permissions.ProductRead,
            },
        };
    }
}
