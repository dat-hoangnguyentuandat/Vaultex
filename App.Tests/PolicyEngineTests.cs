using System.Text.Json;
using App.Domain.Entities;
using App.Infrastructure.Authorization;

namespace App.Tests;

public class PolicyEngineTests
{
    private readonly PolicyEngine _engine = new();

    private static Policy MakePolicy(string resource, string action, List<PolicyCondition> conditions, bool isActive = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            Resource = resource,
            Action = action,
            IsActive = isActive,
            ConditionsJson = JsonSerializer.Serialize(conditions),
            CreatedAt = DateTime.UtcNow
        };

    // ── inactive / mismatch ──────────────────────────────────────────────────

    [Fact]
    public void Evaluate_InactivePolicy_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read", [], isActive: false);
        var ctx = new PolicyEvaluationContext { Resource = "product", Action = "read" };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void Evaluate_ResourceMismatch_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read", []);
        var ctx = new PolicyEvaluationContext { Resource = "order", Action = "read" };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void Evaluate_ActionMismatch_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read", []);
        var ctx = new PolicyEvaluationContext { Resource = "product", Action = "write" };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void Evaluate_NoConditions_ReturnsTrue()
    {
        var policy = MakePolicy("product", "read", []);
        var ctx = new PolicyEvaluationContext { Resource = "product", Action = "read" };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    // ── tenant_match ─────────────────────────────────────────────────────────

    [Fact]
    public void TenantMatch_SameTenant_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "tenant_match", Operator = "eq", Value = "" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            TenantId = tenantId, ResourceTenantId = tenantId
        };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void TenantMatch_DifferentTenant_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "tenant_match", Operator = "eq", Value = "" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            TenantId = Guid.NewGuid(), ResourceTenantId = Guid.NewGuid()
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void TenantMatch_NullTenantId_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "tenant_match", Operator = "eq", Value = "" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            TenantId = null, ResourceTenantId = Guid.NewGuid()
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    // ── owner_only ───────────────────────────────────────────────────────────

    [Fact]
    public void OwnerOnly_UserIsOwner_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        var policy = MakePolicy("product", "delete",
            [new PolicyCondition { Type = "owner_only", Operator = "eq", Value = "" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "delete",
            UserId = userId, ResourceOwnerId = userId
        };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void OwnerOnly_UserIsNotOwner_ReturnsFalse()
    {
        var policy = MakePolicy("product", "delete",
            [new PolicyCondition { Type = "owner_only", Operator = "eq", Value = "" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "delete",
            UserId = Guid.NewGuid(), ResourceOwnerId = Guid.NewGuid()
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void OwnerOnly_NullResourceOwner_ReturnsFalse()
    {
        var policy = MakePolicy("product", "delete",
            [new PolicyCondition { Type = "owner_only", Operator = "eq", Value = "" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "delete",
            UserId = Guid.NewGuid(), ResourceOwnerId = null
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    // ── time_range ───────────────────────────────────────────────────────────

    [Fact]
    public void TimeRange_WithinRange_ReturnsTrue()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "time_range", Operator = "between", Value = "00:00-23:59" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            RequestTime = DateTime.UtcNow
        };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void TimeRange_InvalidFormat_AllowsThrough()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "time_range", Operator = "between", Value = "bad-format" }]);
        var ctx = new PolicyEvaluationContext { Resource = "product", Action = "read" };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void TimeRange_OvernightRange_WorksCorrectly()
    {
        // 22:00-06:00 overnight — pick a time clearly inside (23:00 UTC)
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "time_range", Operator = "between", Value = "22:00-06:00" }]);

        // Force RequestTime to a UTC time whose local equivalent is 23:00
        // Use a fixed UTC time and convert; simplest: just use local 23:00 today
        var localNow = DateTime.Now.Date.AddHours(23);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            RequestTime = localNow.ToUniversalTime()
        };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    // ── ip_whitelist ─────────────────────────────────────────────────────────

    [Fact]
    public void IpWhitelist_ExactMatch_ReturnsTrue()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "ip_whitelist", Operator = "in", Value = "10.0.0.1,192.168.1.5" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            IpAddress = "192.168.1.5"
        };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void IpWhitelist_NotInList_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "ip_whitelist", Operator = "in", Value = "10.0.0.1" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            IpAddress = "10.0.0.2"
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void IpWhitelist_CidrMatch_ReturnsTrue()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "ip_whitelist", Operator = "in", Value = "192.168.1.0/24" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            IpAddress = "192.168.1.100"
        };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void IpWhitelist_CidrNoMatch_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "ip_whitelist", Operator = "in", Value = "192.168.1.0/24" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            IpAddress = "192.168.2.1"
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void IpWhitelist_NullIp_ReturnsFalse()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "ip_whitelist", Operator = "in", Value = "10.0.0.1" }]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "read",
            IpAddress = null
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    // ── multiple conditions ──────────────────────────────────────────────────

    [Fact]
    public void MultipleConditions_AllPass_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var policy = MakePolicy("product", "delete",
        [
            new PolicyCondition { Type = "tenant_match", Operator = "eq", Value = "" },
            new PolicyCondition { Type = "owner_only",   Operator = "eq", Value = "" }
        ]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "delete",
            UserId = userId, ResourceOwnerId = userId,
            TenantId = tenantId, ResourceTenantId = tenantId
        };
        Assert.True(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void MultipleConditions_OneFails_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var policy = MakePolicy("product", "delete",
        [
            new PolicyCondition { Type = "tenant_match", Operator = "eq", Value = "" },
            new PolicyCondition { Type = "owner_only",   Operator = "eq", Value = "" }
        ]);
        var ctx = new PolicyEvaluationContext
        {
            Resource = "product", Action = "delete",
            UserId = userId, ResourceOwnerId = Guid.NewGuid(), // different owner
            TenantId = tenantId, ResourceTenantId = tenantId
        };
        Assert.False(_engine.Evaluate(policy, ctx));
    }

    [Fact]
    public void UnknownConditionType_AllowsThrough()
    {
        var policy = MakePolicy("product", "read",
            [new PolicyCondition { Type = "future_condition", Operator = "eq", Value = "x" }]);
        var ctx = new PolicyEvaluationContext { Resource = "product", Action = "read" };
        Assert.True(_engine.Evaluate(policy, ctx));
    }
}
