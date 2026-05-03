using System.Text;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Infrastructure.Repositories;
using LifeAlertPlus.Infrastructure.Seed;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("Anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<ChatbotService>();
builder.Services.AddSingleton<SimulationManager>();
builder.Services.AddSingleton<ActivityProfileService>();
builder.Services.AddSingleton<ConditionRuleEngine>();
builder.Services.AddSingleton<AlertMonitorService>();
builder.Services.AddScoped<DailyReportService>();
builder.Services.AddHostedService<DailyReportBackgroundService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5254",
                  "https://localhost:5254",
                  "http://localhost:8081",
                  "https://localhost:8081",
                  "https://localhost:8444",
                  "https://client-lifealertplusiot-gqf3crdrenfgd9bw.germanywestcentral-01.azurewebsites.net",
                  "https://client-lifealertplusiot-dxgkd5emgacba2h6.germanywestcentral-01.azurewebsites.net")
              .AllowAnyHeader()
              .AllowAnyMethod()
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
builder.Services.AddSingleton<NearestHospitalService>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IMonitoredService, MonitoredService>();
builder.Services.AddScoped<IUserMonitoredService, UserMonitoredService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IMeasurementService, MeasurementService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<GetUrlService>();
builder.Services.AddScoped<IImportService, ImportService>();

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
    .AddCookie("Cookies")
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
    });

var app = builder.Build();

await UserSeed.SeedAsync(app.Services);

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseCors("AllowBlazorClient");

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();


app.MapControllers();
// SignalR NotificationHub
app.MapHub<LifeAlertPlus.API.Hubs.NotificationHub>("/notificationhub");

app.Run();
