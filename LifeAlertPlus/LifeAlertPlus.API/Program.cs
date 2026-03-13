using Microsoft.AspNetCore.Authentication.Google;
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

builder.Services.AddControllers();
builder.Services.AddHttpClient();

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
                  "https://localhost:8444")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMonitoredRepository, MonitoredRepository>();
builder.Services.AddScoped<IUserMonitoredRepository, UserMonitoredRepository>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IMonitoredService, MonitoredService>();
builder.Services.AddScoped<IUserMonitoredService, UserMonitoredService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<GetUrlService>();

var connString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=lifealert.db";

builder.Services.AddLifeAlertPlusDbContext(connString);

var jwtSettings = builder.Configuration.GetSection("Jwt");

builder.Services
    .AddAuthentication(options =>
    {
        // API requests are authenticated via JWT Bearer.
        // DefaultSignInScheme stays as Cookies so Google OAuth callback
        // can temporarily store the Google identity in a cookie.
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!))
        };
        // Return 401 JSON instead of redirecting to Google OAuth on auth failure.
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
        googleOptions.ClientId = googleAuthNSection["ClientId"]!;
        googleOptions.ClientSecret = googleAuthNSection["ClientSecret"]!;
        googleOptions.CallbackPath = "/signin-google";
        googleOptions.Scope.Add("profile");
    });

var app = builder.Build();

await UserSeed.SeedAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowBlazorClient");

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
