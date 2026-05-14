using App.Application.Interfaces;
using App.Domain.Entities;
using App.Domain.Constants;
using App.Infrastructure.Authorization;
using App.Infrastructure.Configuration;
using App.Infrastructure.Data;
using App.Infrastructure.Services;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Server.AspNetCore;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.Threading.RateLimiting;
using static OpenIddict.Abstractions.OpenIddictConstants;

// Bootstrap logger: captures startup errors before full Serilog is configured
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Vaultex API");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog — read full config from appsettings
    builder.Host.UseSerilog((context, services, config) =>
        config.ReadFrom.Configuration(context.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext());

    // Database
    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
        options.UseOpenIddict();
    });

    // Tenant context — scoped per request
    builder.Services.AddScoped<ITenantContext, TenantContext>();

    // Identity
    builder.Services.AddIdentity<User, AppRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

    // OpenIddict
    builder.Services.AddOpenIddict()
        .AddCore(options =>
        {
            options.UseEntityFrameworkCore()
                   .UseDbContext<AppDbContext>();
        })
        .AddServer(options =>
        {
            options.SetTokenEndpointUris("/connect/token")
                   .SetAuthorizationEndpointUris("/connect/authorize")
                   .SetEndSessionEndpointUris("/connect/logout")
                   .SetIntrospectionEndpointUris("/connect/introspect")
                   .SetJsonWebKeySetEndpointUris("/connect/jwks")
                   .SetConfigurationEndpointUris("/.well-known/openid-configuration");

            options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess);

            options.AllowPasswordFlow()
                   .AllowRefreshTokenFlow()
                   .AllowAuthorizationCodeFlow()
                   .AllowClientCredentialsFlow();

            options.RequireProofKeyForCodeExchange();

            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();

            options.UseAspNetCore()
                   .EnableTokenEndpointPassthrough()
                   .EnableAuthorizationEndpointPassthrough()
                   .EnableEndSessionEndpointPassthrough();

            var tokenLifetimes = builder.Configuration
                .GetSection("OpenIddict:TokenLifetimes")
                .Get<TokenLifetimesSettings>() ?? new();
            options.SetAccessTokenLifetime(TimeSpan.FromSeconds(tokenLifetimes.AccessTokenSeconds));
            options.SetRefreshTokenLifetime(TimeSpan.FromDays(tokenLifetimes.RefreshTokenDays));
            options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(tokenLifetimes.AuthorizationCodeMinutes));
        })
        .AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
        });

    builder.Services.AddSession();
    builder.Services.AddRazorPages();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // CORS — allow App.Web origin (configurable via Cors:AllowedOrigins)
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["https://localhost:7066", "https://localhost:7108"];
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("VaultexCors", policy =>
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Rate limiting — 10 requests/minute per IP on /connect/token
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("TokenEndpoint", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
        options.RejectionStatusCode = 429;
    });

    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IProductService, ProductService>();
    builder.Services.AddScoped<AuditLogService>();
    builder.Services.AddScoped<PolicyEngine>();
    builder.Services.AddScoped<IAuthorizationHandler, PolicyAuthorizationHandler>();
    builder.Services.AddHttpContextAccessor();

    // Redis — optional, gracefully degrades if not configured
    var redisConn = builder.Configuration.GetConnectionString("Redis");
    if (!string.IsNullOrEmpty(redisConn))
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConn));
    }
    builder.Services.AddScoped<TokenBlacklistService>();

    // Authorization Policies
    builder.Services.AddAuthorization(options =>
    {
        // Product permissions
        options.AddPolicy("ProductCreate", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductCreate, App.Domain.Constants.Permissions.AdminAll));

        options.AddPolicy("ProductRead", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductRead, App.Domain.Constants.Permissions.AdminAll));

        options.AddPolicy("ProductUpdate", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductUpdate, App.Domain.Constants.Permissions.AdminAll));

        options.AddPolicy("ProductDelete", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductDelete, App.Domain.Constants.Permissions.AdminAll));

        // User permissions
        options.AddPolicy("UserRead", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.UserRead, App.Domain.Constants.Permissions.AdminAll));

        options.AddPolicy("UserManage", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.UserManage, App.Domain.Constants.Permissions.AdminAll));
    });

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseCors("VaultexCors");
    app.UseRateLimiter();

    // Security headers
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        if (!app.Environment.IsDevelopment())
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        await next();
    });

    // Request logging: logs every HTTP request with method, path, status, elapsed + UserId
    app.UseSerilogRequestLogging(opts =>
    {
        opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
                diagnosticContext.Set("UserId", userId);
        };
    });

    app.UseSession();
    app.UseMiddleware<TenantResolverMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapRazorPages();
    app.MapControllers();

    // Seed
    using (var scope = app.Services.CreateScope())
    {
        await TenantSeeder.SeedAsync(scope.ServiceProvider);
        await OpenIddictSeeder.SeedAsync(scope.ServiceProvider);
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
