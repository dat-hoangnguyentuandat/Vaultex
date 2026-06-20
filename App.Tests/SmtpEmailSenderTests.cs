using App.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace App.Tests;

public class SmtpEmailSenderTests
{
    private static SmtpEmailSender Build(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var logger = new Mock<ILogger<SmtpEmailSender>>().Object;
        return new SmtpEmailSender(configuration, logger);
    }

    [Fact]
    public async Task SendAsync_NoSmtpHost_LogsWarningAndDoesNotThrow()
    {
        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Smtp:Host"] = "" })
            .Build();

        var svc = new SmtpEmailSender(config, loggerMock.Object);

        await svc.SendAsync("user@example.com", "Test Subject", "<p>Hello</p>");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("user@example.com")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_NullSmtpHost_LogsAndDoesNotThrow()
    {
        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var svc = new SmtpEmailSender(config, loggerMock.Object);

        // Should not throw even with no config at all
        await svc.SendAsync("user@example.com", "Reset Password", "<p>Link</p>");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_SmtpConfigured_AttemptsConnection()
    {
        // We can't test actual SMTP sending in unit tests, but we can verify
        // it throws a connection error (not a config error) when host is set.
        var svc = Build(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "smtp.invalid.local",
            ["Email:Smtp:Port"] = "587",
            ["Email:Smtp:Username"] = "user",
            ["Email:Smtp:Password"] = "pass",
            ["Email:From"] = "noreply@vaultex.io"
        });

        // Should throw a network/socket exception, not a NullReferenceException or config error
        var ex = await Record.ExceptionAsync(() =>
            svc.SendAsync("to@example.com", "Subject", "<p>Body</p>"));

        Assert.NotNull(ex);
        Assert.IsNotType<NullReferenceException>(ex);
        Assert.IsNotType<ArgumentNullException>(ex);
    }
}
