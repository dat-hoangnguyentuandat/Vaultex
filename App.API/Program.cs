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
                   .SetUserInfoEndpointUris("/connect/userinfo")
                   .SetJsonWebKeySetEndpointUris("/connect/jwks")
                   .SetConfigurationEndpointUris("/.well-known/openid-configuration");

            options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess);

            options.AllowPasswordFlow()
                   .AllowRefreshTokenFlow()
                   .AllowAuthorizationCodeFlow()
                   .AllowClientCredentialsFlow();

            options.RequireProofKeyForCodeExchange();

            // Access tokens are signed JWTs (JWS) only — third parties verify via JWKS.
            // Encryption (JWE) is unnecessary for tokens consumed by external apps and
            // would require sharing the encryption private key, which is not standard OIDC.
            options.DisableAccessTokenEncryption();

            if (builder.Environment.IsDevelopment())
            {
                options.AddDevelopmentEncryptionCertificate()
                       .AddDevelopmentSigningCertificate();
            }
            else
            {
                // Production cert handling, in priority order:
                //   1. Operator-supplied PFX paths (OpenIddict:Certificates:*)
                //      — use this when running multi-instance behind a load balancer,
                //        or when certs come from a secrets manager / mounted volume.
                //   2. Auto-provisioned self-signed PFX persisted to disk.
                //      — first run generates 5-year RSA-2048 certs; subsequent runs reuse.
                //
                // Ephemeral keys are NEVER used in production (they invalidate all tokens
                // on every restart, breaking active user sessions).
                var encryptionCertPath = builder.Configuration["OpenIddict:Certificates:EncryptionCertPath"];
                var signingCertPath = builder.Configuration["OpenIddict:Certificates:SigningCertPath"];
                var certPassword = builder.Configuration["OpenIddict:Certificates:Password"] ?? "";

                if (string.IsNullOrEmpty(encryptionCertPath) || string.IsNullOrEmpty(signingCertPath))
                {
                    // Fall back to auto-provisioning under content root.
                    var certDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "certs");
                    encryptionCertPath ??= Path.Combine(certDir, "encryption.pfx");
                    signingCertPath ??= Path.Combine(certDir, "signing.pfx");

                    if (string.IsNullOrEmpty(certPassword))
                    {
                        // Persist the generated password alongside the certs so they remain readable
                        // across restarts. Only used when operator hasn't provided an explicit password.
                        var passwordPath = Path.Combine(certDir, ".cert-password");
                        if (File.Exists(passwordPath))
                        {
                            certPassword = File.ReadAllText(passwordPath).Trim();
                        }
                        else
                        {
                            certPassword = Convert.ToBase64String(
                                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                            Directory.CreateDirectory(certDir);
                            File.WriteAllText(passwordPath, certPassword);
                            if (!OperatingSystem.IsWindows())
                            {
                                try { File.SetUnixFileMode(passwordPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                                catch { }
                            }
                        }
                    }
                }

                options.AddEncryptionCertificate(
                    OpenIddictCertificateProvisioner.GetOrCreateEncryptionCertificate(
                        encryptionCertPath, certPassword));
                options.AddSigningCertificate(
                    OpenIddictCertificateProvisioner.GetOrCreateSigningCertificate(
                        signingCertPath, certPassword));
            }

            options.UseAspNetCore()
                   .EnableTokenEndpointPassthrough()
                   .EnableAuthorizationEndpointPassthrough()
                   .EnableUserInfoEndpointPassthrough()
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
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()));
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

    // CORS — two policies:
    //   "VaultexCors"  — for our own SPA (/api/*), restricted origins, allows credentials.
    //   "OidcPublic"   — for OIDC public endpoints (/connect/*, /.well-known/*),
    //                    open to any origin so third-party SPAs can complete the auth code
    //                    + PKCE flow. No credentials (cookies) are used on these endpoints,
    //                    so AllowAnyOrigin is safe — clients are authenticated via PKCE
    //                    and client_secret in the request body.
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

        options.AddPolicy("OidcPublic", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .WithMethods("GET", "POST", "OPTIONS")
                  .WithExposedHeaders("WWW-Authenticate");
        });
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("TokenEndpoint", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.AddPolicy("LoginSubmit", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.AddPolicy("RegistrationSubmit", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.AddPolicy("PasswordRecovery", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.AddPolicy("ConfirmationEmail", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.RejectionStatusCode = 429;
    });

    builder.Services.AddScoped<IAuthService, AuthService>();
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

    // OIDC public endpoints (/connect/*, /.well-known/*) → open CORS, no credentials.
    // Everything else → restricted origins with credentials.
    app.UseWhen(
        ctx => ctx.Request.Path.StartsWithSegments("/connect") ||
               ctx.Request.Path.StartsWithSegments("/.well-known"),
        branch => branch.UseCors("OidcPublic"));
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/connect") &&
               !ctx.Request.Path.StartsWithSegments("/.well-known"),
        branch => branch.UseCors("VaultexCors"));

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
    app.MapGet("/", () => Results.Redirect("/account/login"));
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
