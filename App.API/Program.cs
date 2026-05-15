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
        options.SignIn.RequireConfirmedEmail = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
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

            if (builder.Environment.IsDevelopment())
            {
                options.AddDevelopmentEncryptionCertificate()
                       .AddDevelopmentSigningCertificate();
            }
            else
            {
                var encryptionCertPath = builder.Configuration["OpenIddict:Certificates:EncryptionCertPath"];
                var signingCertPath = builder.Configuration["OpenIddict:Certificates:SigningCertPath"];
                var certPassword = builder.Configuration["OpenIddict:Certificates:Password"] ?? "";

                if (!string.IsNullOrEmpty(encryptionCertPath) && !string.IsNullOrEmpty(signingCertPath))
                {
                    options.AddEncryptionCertificate(
                        new System.Security.Cryptography.X509Certificates.X509Certificate2(
                            encryptionCertPath, certPassword));
                    options.AddSigningCertificate(
                        new System.Security.Cryptography.X509Certificates.X509Certificate2(
                            signingCertPath, certPassword));
                }
                else
                {
                    // Fallback: ephemeral keys — tokens won't survive restarts, but app won't crash
                    options.AddEphemeralEncryptionKey()
                           .AddEphemeralSigningKey();
                }
            }

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
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Enter your access token (without 'Bearer ' prefix)"
        });
        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

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

    // Rate limiting — 10 requests/minute per IP on /connect/token; 5/min on login page
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

        options.AddPolicy("LoginPage", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.RejectionStatusCode = 429;
    });

    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IProductService, ProductService>();
    builder.Services.AddScoped<AuditLogService>();
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
    builder.Services.AddScoped<PolicyEngine>();
    builder.Services.AddScoped<IAuthorizationHandler, PolicyAuthorizationHandler>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHealthChecks();

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
        // Product permissions — RBAC claim check + PBAC engine (PolicyRequirement)
        options.AddPolicy("ProductCreate", policy =>
        {
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductCreate, App.Domain.Constants.Permissions.AdminAll);
            policy.AddRequirements(new App.Infrastructure.Authorization.PolicyRequirement("product", "create"));
        });

        options.AddPolicy("ProductRead", policy =>
        {
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductRead, App.Domain.Constants.Permissions.AdminAll);
            policy.AddRequirements(new App.Infrastructure.Authorization.PolicyRequirement("product", "read"));
        });

        options.AddPolicy("ProductUpdate", policy =>
        {
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductUpdate, App.Domain.Constants.Permissions.AdminAll);
            policy.AddRequirements(new App.Infrastructure.Authorization.PolicyRequirement("product", "update"));
        });

        options.AddPolicy("ProductDelete", policy =>
        {
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.ProductDelete, App.Domain.Constants.Permissions.AdminAll);
            policy.AddRequirements(new App.Infrastructure.Authorization.PolicyRequirement("product", "delete"));
        });

        // User permissions
        options.AddPolicy("UserRead", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.UserRead, App.Domain.Constants.Permissions.AdminAll));

        options.AddPolicy("UserManage", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.UserManage, App.Domain.Constants.Permissions.AdminAll));

        // Platform permissions
        options.AddPolicy("PlatformAdmin", policy =>
            policy.RequireClaim("permission", App.Domain.Constants.Permissions.PlatformAdmin));
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
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none';";
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
    app.UseMiddleware<App.Infrastructure.Middleware.TokenBlacklistMiddleware>();
    app.UseAuthorization();
    app.MapRazorPages();
    app.MapControllers();
    app.MapHealthChecks("/health");

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
