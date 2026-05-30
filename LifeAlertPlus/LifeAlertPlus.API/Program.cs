using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.ResponseCompression;
using System.Threading.RateLimiting;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Infrastructure.Repositories;
using LifeAlertPlus.Infrastructure.Seed;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

// Npgsql ≥6 rejects DateTime with Kind=Unspecified for `timestamp with time zone`.
// Our DTOs (Birthdate, dates parsed from JSON) come in as Unspecified, so we opt
// into the legacy "Unspecified is treated as UTC" behavior for the whole process.
// Must be set before the first Npgsql connection is created.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.Providers.Add<BrotliCompressionProvider>();
    opts.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(opts => opts.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(opts => opts.Level = CompressionLevel.Fastest);

builder.Services.AddSingleton<IPushNotificationService, PushNotificationService>();
// SignalR
builder.Services.AddSignalR();

// Twilio Service
builder.Services.AddScoped<ITwilioService, LifeAlertPlus.Infrastructure.Services.TwilioService>();

builder.Services.AddControllers();
builder.Services.AddHttpClient("AiService", client =>
{
    var aiBaseUrl = builder.Configuration["Urls:AiServiceUrl"] ?? "http://localhost:8000";
    client.BaseAddress = new Uri(aiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddHttpClient("Anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("Overpass", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<ChatbotService>();
builder.Services.AddSingleton<SimulationManager>();
builder.Services.AddSingleton<ActivityProfileService>();
builder.Services.AddSingleton<ConditionRuleEngine>();
builder.Services.AddSingleton<AlertMonitorService>();
builder.Services.AddScoped<DailyReportService>();
builder.Services.AddHostedService<DailyReportBackgroundService>();
builder.Services.AddScoped<RetentionCleanupService>();
builder.Services.AddHostedService<RetentionCleanupBackgroundService>();
builder.Services.AddHostedService<ActivityProfileRebuildBackgroundService>();
builder.Services.AddSingleton<AuditService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
              .AllowCredentials();
    });
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMonitoredRepository, MonitoredRepository>();
builder.Services.AddScoped<IUserMonitoredRepository, UserMonitoredRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IMeasurementRepository, MeasurementRepository>();
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
builder.Services.AddScoped<IActivityProfileRepository, ActivityProfileRepository>();
builder.Services.AddScoped<IMonitoredConditionRepository, MonitoredConditionRepository>();
builder.Services.AddScoped<IWifiNetworkRepository, WifiNetworkRepository>();
builder.Services.AddSingleton<NearestHospitalService>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IMonitoredService, MonitoredService>();
builder.Services.AddScoped<IUserMonitoredService, UserMonitoredService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IMeasurementService, MeasurementService>();
builder.Services.AddScoped<IWifiNetworkService, WifiNetworkService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<GetUrlService>();

var connString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=lifealert.db";

builder.Services.AddLifeAlertPlusDbContext(connString);

var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"];
if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("Jwt:Key is not configured. Set it in Azure App Settings as Jwt__Key.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = "Cookies";
    })
    .AddCookie("Cookies", options =>
    {
        // The Google handler temporarily signs in to this scheme between /signin-google
        // and our /google-response action. SameSite=Lax ensures the auth cookie survives
        // the top-level redirect Google issues back to our domain. SecurePolicy=SameAsRequest
        // keeps it working in HTTP-only local dev while still going Secure in production.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.HttpOnly = true;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            // SignalR WebSocket connections carry the token in the query string, not the header.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/notificationhub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"success\":false,\"message\":\"Unauthorized. Token is missing or expired.\"}");
            }
        };
    })
    .AddGoogle(googleOptions =>
    {
        var googleAuthNSection = builder.Configuration.GetSection("Authentication:Google");
        googleOptions.ClientId = googleAuthNSection["ClientId"] ?? "";
        googleOptions.ClientSecret = googleAuthNSection["ClientSecret"] ?? "";
        googleOptions.CallbackPath = "/signin-google";
        googleOptions.Scope.Add("profile");
        // Correlation cookie travels back from Google. Lax is required so the browser
        // includes it on the top-level redirect from accounts.google.com → /signin-google.
        // Without an explicit policy, recent Chromium builds can drop it and produce
        // a "correlation failed" → silent redirect to /login.
        googleOptions.CorrelationCookie.SameSite = SameSiteMode.Lax;
        googleOptions.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

var app = builder.Build();

try
{
    await UserSeed.SeedAsync(app.Services);
}
catch (Exception seedEx)
{
    var seedLogger = app.Services.GetRequiredService<ILogger<Program>>();
    seedLogger.LogError(seedEx, "Database seed/migration failed at startup — app will continue. Check ConnectionStrings__Default in Azure App Settings.");
}

// Must run before any other middleware so that X-Forwarded-Proto / X-Forwarded-For
// from Azure's reverse proxy are honoured. Without this the OAuth middleware generates
// http:// callback URIs that don't match the https:// URI registered in Google Console.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Must be first: catches unhandled exceptions and re-applies CORS so the browser
// can read the error body instead of seeing an opaque CORS failure on top of a 500.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var corsService    = context.RequestServices.GetRequiredService<ICorsService>();
        var policyProvider = context.RequestServices.GetRequiredService<ICorsPolicyProvider>();
        var policy         = await policyProvider.GetPolicyAsync(context, "AllowBlazorClient");
        if (policy is not null)
        {
            var corsResult = corsService.EvaluatePolicy(context, policy);
            corsService.ApplyResult(corsResult, context.Response);
        }

        context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        var ex      = feature?.Error;
        var logger  = context.RequestServices.GetService<ILogger<Program>>();
        logger?.LogError(ex, "Unhandled exception");

        // Write to SystemError log for the admin ErrorLog page.
        try
        {
            var audit = context.RequestServices.GetService<LifeAlertPlus.API.Services.AuditService>();
            audit?.LogErrorAsync(
                source:  feature?.Path ?? "Unknown",
                message: ex?.Message ?? "Unhandled exception",
                details: ex?.ToString() ?? string.Empty,
                level:   "Error");
        }
        catch { /* best-effort — don't let audit writing crash the error handler */ }

        await context.Response.WriteAsync("{\"success\":false,\"message\":\"An internal server error occurred.\"}");
    });
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowBlazorClient");

app.UseResponseCompression();
app.UseRateLimiter();
app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// SignalR NotificationHub
app.MapHub<LifeAlertPlus.API.Hubs.NotificationHub>("/notificationhub");

app.Run();
