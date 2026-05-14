using System.Text.Json;
using App.Domain.Entities;

namespace App.Infrastructure.Authorization
{
    public class PolicyEvaluationContext
    {
        public Guid UserId { get; set; }
        public Guid? TenantId { get; set; }
        public Guid? ResourceOwnerId { get; set; }
        public Guid? ResourceTenantId { get; set; }
        public string? IpAddress { get; set; }
        public string Resource { get; set; } = "";
        public string Action { get; set; } = "";
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    }

    public class PolicyEngine
    {
        public bool Evaluate(Policy policy, PolicyEvaluationContext ctx)
        {
            if (!policy.IsActive) return false;
            if (policy.Resource != ctx.Resource || policy.Action != ctx.Action) return false;

            var conditions = JsonSerializer.Deserialize<List<PolicyCondition>>(policy.ConditionsJson)
                ?? [];

            return conditions.All(c => EvaluateCondition(c, ctx));
        }

        private bool EvaluateCondition(PolicyCondition condition, PolicyEvaluationContext ctx)
        {
            return condition.Type switch
            {
                "tenant_match" => EvaluateTenantMatch(ctx),
                "owner_only"   => EvaluateOwnerOnly(ctx),
                "time_range"   => EvaluateTimeRange(condition, ctx),
                "ip_whitelist" => EvaluateIpWhitelist(condition, ctx),
                _              => true
            };
        }

        private bool EvaluateTenantMatch(PolicyEvaluationContext ctx)
        {
            if (!ctx.TenantId.HasValue || !ctx.ResourceTenantId.HasValue) return false;
            return ctx.TenantId == ctx.ResourceTenantId;
        }

        private bool EvaluateOwnerOnly(PolicyEvaluationContext ctx)
        {
            if (!ctx.ResourceOwnerId.HasValue) return false;
            return ctx.UserId == ctx.ResourceOwnerId;
        }

        // Value format: "09:00-17:00" (local time, 24h)
        private bool EvaluateTimeRange(PolicyCondition condition, PolicyEvaluationContext ctx)
        {
            var parts = condition.Value.Split('-');
            if (parts.Length != 2) return true;

            if (!TimeOnly.TryParse(parts[0].Trim(), out var start) ||
                !TimeOnly.TryParse(parts[1].Trim(), out var end))
                return true;

            var now = TimeOnly.FromDateTime(ctx.RequestTime.ToLocalTime());
            return start <= end
                ? now >= start && now <= end
                : now >= start || now <= end; // overnight range
        }

        // Value format: comma-separated CIDRs or IPs, e.g. "192.168.1.0/24,10.0.0.1"
        private bool EvaluateIpWhitelist(PolicyCondition condition, PolicyEvaluationContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.IpAddress)) return false;

            var allowed = condition.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim());

            return allowed.Any(entry => IpMatches(ctx.IpAddress, entry));
        }

        private bool IpMatches(string ip, string entry)
        {
            if (!entry.Contains('/'))
                return ip == entry;

            // CIDR match
            var cidrParts = entry.Split('/');
            if (cidrParts.Length != 2 || !int.TryParse(cidrParts[1], out var prefixLen))
                return false;

            if (!System.Net.IPAddress.TryParse(ip, out var requestIp) ||
                !System.Net.IPAddress.TryParse(cidrParts[0], out var networkIp))
                return false;

            var requestBytes = requestIp.GetAddressBytes();
            var networkBytes = networkIp.GetAddressBytes();
            if (requestBytes.Length != networkBytes.Length) return false;

            var fullBytes = prefixLen / 8;
            var remainBits = prefixLen % 8;

            for (int i = 0; i < fullBytes; i++)
                if (requestBytes[i] != networkBytes[i]) return false;

            if (remainBits > 0)
            {
                var mask = (byte)(0xFF << (8 - remainBits));
                if ((requestBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                    return false;
            }

            return true;
        }
    }
}
