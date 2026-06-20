using App.Application.DTOs;
using App.Application.Interfaces;
using App.Domain.Entities;
using App.Domain.Exceptions;
using App.Infrastructure.Data;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace App.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PlatformDbContext _platformDb;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<RoleManager<AppRole>> _roleManagerMock;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly IConfiguration _config;
    private readonly AuditLogService _auditLog;
    private readonly AuthService _svc;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _platformDb = new PlatformDbContext(platformOptions);

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            store.Object,
            new Mock<IOptions<IdentityOptions>>().Object,
            new Mock<IPasswordHasher<User>>().Object,
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new Mock<ILookupNormalizer>().Object,
            new Mock<IdentityErrorDescriber>().Object,
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<User>>>().Object);

        var roleStore = new Mock<IRoleStore<AppRole>>();
        _roleManagerMock = new Mock<RoleManager<AppRole>>(
            roleStore.Object,
            Array.Empty<IRoleValidator<AppRole>>(),
            new Mock<ILookupNormalizer>().Object,
            new Mock<IdentityErrorDescriber>().Object,
            new Mock<ILogger<RoleManager<AppRole>>>().Object);

        _emailSenderMock = new Mock<IEmailSender>();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://localhost:7108"
            })
            .Build();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        _auditLog = new AuditLogService(_db, httpAccessor.Object, new Mock<ILogger<AuditLogService>>().Object);

        _svc = new AuthService(
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _emailSenderMock.Object,
            _config,
            _auditLog,
            new PlatformUserDirectoryService(_platformDb));
    }

    public void Dispose()
    {
        _db.Dispose();
        _platformDb.Dispose();
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_UserNotFound_DoesNotSendEmail()
    {
        _userManagerMock.Setup(m => m.FindByEmailAsync("unknown@example.com"))
            .ReturnsAsync((User?)null);

        await _svc.SendEmailConfirmationAsync("unknown@example.com");

        _emailSenderMock.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_AlreadyConfirmed_DoesNotSendEmail()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "confirmed@example.com",
            UserName = "confirmed@example.com",
            EmailConfirmed = true
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);

        await _svc.SendEmailConfirmationAsync(user.Email);

        _emailSenderMock.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_UnconfirmedUser_SendsEmailWithConfirmLink()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "new@example.com",
            UserName = "new@example.com",
            EmailConfirmed = false
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GenerateEmailConfirmationTokenAsync(user))
            .ReturnsAsync("test-token-123");

        string? capturedBody = null;
        _emailSenderMock
            .Setup(e => e.SendAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);

        await _svc.SendEmailConfirmationAsync(user.Email);

        _emailSenderMock.Verify(
            e => e.SendAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);

        Assert.NotNull(capturedBody);
        Assert.Contains("/account/confirm-email", capturedBody);
        Assert.Contains("new%40example.com", capturedBody);
    }

    [Fact]
    public async Task ForgotPasswordAsync_UnknownEmail_DoesNotSendEmail()
    {
        _userManagerMock.Setup(m => m.FindByEmailAsync("ghost@example.com"))
            .ReturnsAsync((User?)null);

        await _svc.ForgotPasswordAsync(new ForgotPasswordDto { Email = "ghost@example.com" });

        _emailSenderMock.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordAsync_KnownEmail_SendsResetLink()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            UserName = "user@example.com"
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GeneratePasswordResetTokenAsync(user))
            .ReturnsAsync("reset-token-abc");

        string? capturedBody = null;
        _emailSenderMock
            .Setup(e => e.SendAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => capturedBody = body)
            .Returns(Task.CompletedTask);

        await _svc.ForgotPasswordAsync(new ForgotPasswordDto { Email = user.Email });

        _emailSenderMock.Verify(
            e => e.SendAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);

        Assert.NotNull(capturedBody);
        Assert.Contains("/account/reset-password", capturedBody);
    }

    [Fact]
    public async Task ResetPasswordAsync_UnknownEmail_Throws()
    {
        _userManagerMock.Setup(m => m.FindByEmailAsync("ghost@example.com"))
            .ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _svc.ResetPasswordAsync("ghost@example.com", "token", new ResetPasswordDto { Password = "NewPass@1" }));
    }

    [Fact]
    public async Task ResetPasswordAsync_Success_LogsAuditEvent()
    {
        var tenantId = Guid.NewGuid();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            UserName = "user@example.com",
            TenantId = tenantId
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.ResetPasswordAsync(user, "valid-token", "NewPass@1"))
            .ReturnsAsync(IdentityResult.Success);

        await _svc.ResetPasswordAsync(user.Email, "valid-token", new ResetPasswordDto { Password = "NewPass@1" });

        var log = _db.AuditLogs.SingleOrDefault(a => a.EventType == AuditEventTypes.PasswordReset);
        Assert.NotNull(log);
        Assert.Equal(user.Id, log.UserId);
        Assert.Equal(tenantId, log.TenantId);
    }
}
