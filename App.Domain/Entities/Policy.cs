namespace App.Domain.Entities
{
    public class Policy
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Name { get; set; } = "";
        public string Resource { get; set; } = "";
        public string Action { get; set; } = "";
        public string ConditionsJson { get; set; } = "[]";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Tenant Tenant { get; set; } = null!;
    }

    public class PolicyCondition
    {
        public string Type { get; set; } = "";
        public string Operator { get; set; } = "eq";
        public string Value { get; set; } = "";
    }
}
