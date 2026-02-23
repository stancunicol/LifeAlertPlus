using Microsoft.AspNetCore.Authentication.Google;
using System.Text;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Application.Services;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using LifeAlertPlus.Infrastructure.Repositories;
using LifeAlertPlus.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var connString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=lifealert.db";

builder.Services.AddLifeAlertPlusDbContext(connString);

var jwtSettings = builder.Configuration.GetSection("Jwt");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Cookies";
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
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

UserSeed.Seed(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowBlazorClient");

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

app.Run();
