// Importuri pentru compresia răspunsurilor HTTP (Brotli, Gzip)
using System.IO.Compression;
using System.Text;
// Namespace-uri ASP.NET Core necesare pentru diverse funcționalități
using Microsoft.AspNetCore.ResponseCompression;
using System.Threading.RateLimiting;
// Serviciile definite în proiectul API
using LifeAlertPlus.API.Services;
// Interfețe și implementări din celelalte straturi ale aplicației
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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Npgsql ≥6 rejects DateTime with Kind=Unspecified for `timestamp with time zone`.
// Our DTOs (Birthdate, dates parsed from JSON) come in as Unspecified, so we opt
// into the legacy "Unspecified is treated as UTC" behavior for the whole process.
// Must be set before the first Npgsql connection is created.
// Setare globală: tratăm datele DateTime fără fus orar ca UTC (compatibilitate PostgreSQL)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Creăm constructorul aplicației web (punctul de intrare în configurarea ASP.NET Core)
var builder = WebApplication.CreateBuilder(args);

// Activăm compresia răspunsurilor HTTP pentru a reduce dimensiunea datelor trimise clientului
builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true; // Comprimăm și cererile HTTPS (nu doar HTTP)
    opts.Providers.Add<BrotliCompressionProvider>(); // Adăugăm algoritmul Brotli (cel mai eficient)
    opts.Providers.Add<GzipCompressionProvider>(); // Adăugăm și Gzip ca alternativă
});
// Configurăm nivelul de compresie la "Fastest" (viteza > mărimea compresiei)
builder.Services.Configure<BrotliCompressionProviderOptions>(opts => opts.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(opts => opts.Level = CompressionLevel.Fastest);

// Înregistrăm serviciul de notificări push ca Singleton (o singură instanță pentru toată aplicația)
builder.Services.AddSingleton<IPushNotificationService, PushNotificationService>();
// Activăm SignalR pentru comunicare în timp real (notificări live în browser)
builder.Services.AddSignalR();

// Înregistrăm serviciul Twilio (trimitere SMS-uri de alertă) ca Scoped (câte o instanță per cerere HTTP)
builder.Services.AddScoped<ITwilioService, LifeAlertPlus.Infrastructure.Services.TwilioService>();

// Activăm suportul pentru controllere (endpoint-urile API REST)
builder.Services.AddControllers();
// Client HTTP pentru serviciul de AI intern (chatbot, analize)
builder.Services.AddHttpClient("AiService", client =>
{
    var aiBaseUrl = builder.Configuration["Urls:AiServiceUrl"] ?? "http://localhost:8000"; // URL din configurare sau localhost implicit
    client.BaseAddress = new Uri(aiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10); // Timeout de 10 secunde pentru AI
});
// Client HTTP pentru API-ul Anthropic (Claude AI)
builder.Services.AddHttpClient("Anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.Timeout = TimeSpan.FromSeconds(30); // Timeout mai mare pentru AI extern
});
// Client HTTP pentru Overpass API (găsirea spitalelor din apropiere pe hartă)
builder.Services.AddHttpClient("Overpass", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
// Serviciul de chatbot (conversații AI cu utilizatorul)
builder.Services.AddScoped<ChatbotService>();
// SimulationManager gestionează simulările de date ESP (Singleton + interfață)
builder.Services.AddSingleton<SimulationManager>();
builder.Services.AddSingleton<ISimulationManager>(sp => sp.GetRequiredService<SimulationManager>());
// Profilul de activitate al persoanei monitorizate (dormit, mers etc.)
builder.Services.AddSingleton<ActivityProfileService>();
// Motorul de reguli medicale (evaluează severitatea alertelor în funcție de boli)
builder.Services.AddSingleton<ConditionRuleEngine>();
// Serviciul principal de monitorizare a alertelor vitale
builder.Services.AddSingleton<AlertMonitorService>();
builder.Services.AddSingleton<IAlertMonitorService>(sp => sp.GetRequiredService<AlertMonitorService>());
// Serviciul de rapoarte zilnice (Scoped = instanță nouă per execuție job)
builder.Services.AddScoped<DailyReportService>();
// Serviciu de fundal care trimite rapoarte zilnice automat
builder.Services.AddHostedService<DailyReportBackgroundService>();
// Serviciul de curățare date vechi din baza de date
builder.Services.AddScoped<RetentionCleanupService>();
// Job de fundal care rulează periodic ștergerea datelor expirate
builder.Services.AddHostedService<RetentionCleanupBackgroundService>();
// Job de fundal care reconstruiește profilurile de activitate
builder.Services.AddHostedService<ActivityProfileRebuildBackgroundService>();
// Serviciul de audit (logare acțiuni utilizatori + erori sistem)
builder.Services.AddSingleton<AuditService>();
// Serviciul de logare a testelor dispozitivelor ESP
builder.Services.AddSingleton<DeviceTestLogService>();
builder.Services.AddSingleton<IDeviceTestLogService>(sp => sp.GetRequiredService<DeviceTestLogService>());

// Activăm explorarea endpoint-urilor pentru documentație automată
builder.Services.AddEndpointsApiExplorer();
// Activăm Swagger UI (interfața web pentru testarea manuală a API-ului)
builder.Services.AddSwaggerGen();

// Configurăm rate limiting (limitarea numărului de cereri per minut)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // Răspuns 429 când se depășește limita
    // Limita pentru endpoint-urile de autentificare: max 10 cereri/minut
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10; // Maxim 10 cereri
        opt.Window = TimeSpan.FromMinutes(1); // Per minut
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; // Procesăm cererile în ordine
        opt.QueueLimit = 0; // Nu permitem cozi de așteptare (respingem imediat)
    });
});

// Citim lista originilor permise pentru CORS din fișierul de configurare
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
// Configurăm CORS (Cross-Origin Resource Sharing) — permite Blazor WASM să comunice cu API-ul
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.WithOrigins(corsOrigins) // Doar originile din configurare pot accesa API-ul
              .AllowAnyHeader() // Permitem orice header HTTP
              .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS") // Metodele HTTP permise
              .AllowCredentials(); // Permitem cookie-uri/credențiale (necesar pentru SignalR + JWT)
    });
});

// Înregistrăm toate repository-urile (accesul la baza de date) ca Scoped
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMonitoredRepository, MonitoredRepository>();
builder.Services.AddScoped<IUserMonitoredRepository, UserMonitoredRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IMeasurementRepository, MeasurementRepository>();
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
builder.Services.AddScoped<IActivityProfileRepository, ActivityProfileRepository>();
builder.Services.AddScoped<IMonitoredConditionRepository, MonitoredConditionRepository>();
builder.Services.AddScoped<IWifiNetworkRepository, WifiNetworkRepository>();
// Serviciul de găsire a celui mai apropiat spital (Singleton — cache-ul e în memorie)
builder.Services.AddSingleton<NearestHospitalService>();

// Înregistrăm toate serviciile de business logic ca Scoped
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IJwtService, JwtService>(); // Generare/validare token-uri JWT
builder.Services.AddScoped<IEmailService, EmailService>(); // Trimitere email-uri
builder.Services.AddScoped<IMonitoredService, MonitoredService>(); // Persoanele monitorizate
builder.Services.AddScoped<IUserMonitoredService, UserMonitoredService>(); // Relația user-monitorizat
builder.Services.AddScoped<IRoleService, RoleService>(); // Roluri utilizatori (Admin, User)
builder.Services.AddScoped<IMeasurementService, MeasurementService>(); // Măsurători vitale
builder.Services.AddScoped<IWifiNetworkService, WifiNetworkService>(); // Rețele WiFi salvate pe dispozitiv
builder.Services.AddHttpContextAccessor(); // Acces la contextul HTTP curent (necesar pentru GetUrlService)
builder.Services.AddScoped<GetUrlService>(); // Serviciu care construiește URL-urile corecte (API/client)

// Citim string-ul de conexiune la baza de date din configurare
var connString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=lifealert.db";

// Înregistrăm contextul Entity Framework (legătura cu baza de date)
builder.Services.AddLifeAlertPlusDbContext(connString);

// Citim setările JWT din fișierul de configurare
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"];
// Dacă cheia JWT lipsește, aplicația nu pornește — cheia e obligatorie pentru securitate
if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("Jwt:Key is not configured. Set it in Azure App Settings as Jwt__Key.");

// Configurăm autentificarea cu mai multe scheme
builder.Services
    .AddAuthentication(options =>
    {
        // Schema implicită = JWT Bearer (token-ul trimis în header-ul Authorization)
        options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = "Cookies"; // Google OAuth folosește cookie-uri temporar la autentificare
    })
    .AddCookie("Cookies", options =>
    {
        // The Google handler temporarily signs in to this scheme between /signin-google
        // and our /google-response action. SameSite=Lax ensures the auth cookie survives
        // the top-level redirect Google issues back to our domain. SecurePolicy=SameAsRequest
        // keeps it working in HTTP-only local dev while still going Secure in production.
        options.Cookie.SameSite = SameSiteMode.Lax; // Necestar pentru redirect-ul de la Google
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Secure în producție, flexibil în dev
        options.Cookie.HttpOnly = true; // Cookie-ul nu e accesibil din JavaScript (securitate)
    })
    .AddJwtBearer(options =>
    {
        // Definim regulile de validare a token-urilor JWT
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, // Verificăm cine a emis token-ul
            ValidateAudience = true, // Verificăm pentru cine e destinat token-ul
            ValidateLifetime = true, // Verificăm că token-ul nu a expirat
            ValidateIssuerSigningKey = true, // Verificăm semnătura criptografică
            ValidIssuer = jwtSettings["Issuer"], // Emitentul valid (ex: "LifeAlertPlus")
            ValidAudience = jwtSettings["Audience"], // Destinatarul valid (ex: "LifeAlertPlusUsers")
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)) // Cheia secretă de semnare
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            // SignalR WebSocket connections carry the token in the query string, not the header.
            // SignalR nu poate trimite headere — token-ul vine în query string (?access_token=...)
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"]; // Extragem token-ul din URL
                // Aplicăm această logică doar pentru conexiunile la NotificationHub
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/notificationhub"))
                {
                    context.Token = accessToken; // Îi spunem framework-ului să folosească acest token
                }
                return Task.CompletedTask;
            },
            // Când autentificarea eșuează, returnăm JSON în loc de HTML (mai ușor de procesat de client)
            OnChallenge = context =>
            {
                context.HandleResponse(); // Prevenim răspunsul implicit
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"success\":false,\"message\":\"Unauthorized. Token is missing or expired.\"}");
            }
        };
    })
    // Configurăm autentificarea cu Google OAuth
    .AddGoogle(googleOptions =>
    {
        var googleAuthNSection = builder.Configuration.GetSection("Authentication:Google");
        googleOptions.ClientId = googleAuthNSection["ClientId"] ?? ""; // ID-ul aplicației din Google Console
        googleOptions.ClientSecret = googleAuthNSection["ClientSecret"] ?? ""; // Secretul aplicației
        googleOptions.CallbackPath = "/signin-google"; // URL-ul la care Google redirecționează după login
        googleOptions.Scope.Add("profile"); // Cerem și profilul utilizatorului (nu doar email-ul)
        // Correlation cookie travels back from Google. Lax is required so the browser
        // includes it on the top-level redirect from accounts.google.com → /signin-google.
        // Without an explicit policy, recent Chromium builds can drop it and produce
        // a "correlation failed" → silent redirect to /login.
        googleOptions.CorrelationCookie.SameSite = SameSiteMode.Lax; // Necessar pentru flow-ul OAuth
        googleOptions.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Construim aplicația web (finalizăm configurarea)
var app = builder.Build();

// Step 1: apply migrations first, separately from seed data
// Pasul 1: Aplicăm migrările (actualizăm schema bazei de date)
try
{
    using var migScope = app.Services.CreateScope(); // Cream un scope pentru a accesa serviciile
    var db = migScope.ServiceProvider.GetRequiredService<LifeAlertPlus.Infrastructure.Context.LifeAlertPlusDbContext>();
    var startLogger = migScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    startLogger.LogInformation("Applying pending migrations...");
    await db.Database.MigrateAsync(); // Aplicăm toate migrările neprocesate
    startLogger.LogInformation("Migrations applied successfully.");
}
catch (Exception migEx)
{
    // Dacă migrările eșuează, logăm eroarea dar continuăm (aplicația pornește oricum)
    var migLogger = app.Services.GetRequiredService<ILogger<Program>>();
    migLogger.LogError(migEx, "MIGRATION FAILED — check ConnectionStrings__Default in Azure App Settings. Connection string in use: {ConnString}",
        builder.Configuration.GetConnectionString("Default")?.Split(';').FirstOrDefault() ?? "not set");
}

// Step 2: seed data (idempotent)
// Pasul 2: Populăm baza de date cu date inițiale (admin, roluri etc.) — operație idempotentă
try
{
    await UserSeed.SeedAsync(app.Services); // Dacă datele există deja, nu le duplică
}
catch (Exception seedEx)
{
    var seedLogger = app.Services.GetRequiredService<ILogger<Program>>();
    seedLogger.LogError(seedEx, "Seed failed — app will continue without seed data.");
}

// VAPID configuration check — warn early if keys are missing so push notifications never silently fail
// Verificăm la startup că cheile VAPID pentru Web Push sunt configurate
{
    var vapidLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var vapidPub  = builder.Configuration["WebPush:VapidPublicKey"]; // Cheia publică VAPID
    var vapidPriv = builder.Configuration["WebPush:VapidPrivateKey"]; // Cheia privată VAPID
    if (string.IsNullOrEmpty(vapidPub) || string.IsNullOrEmpty(vapidPriv))
        vapidLogger.LogWarning(
            "VAPID keys are NOT configured. Web Push notifications will be disabled. " +
            "Set WebPush__VapidPublicKey and WebPush__VapidPrivateKey in Azure App Service Configuration.");
    else
        vapidLogger.LogInformation("VAPID keys detected — Web Push notifications are enabled.");
}

// Step 3: pre-populează SimulationManager cu ultimele măsurători din DB.
// Fără acest pas, după orice restart utilizatorii văd "no data" pentru câteva secunde.
try
{
    var simManager = app.Services.GetRequiredService<LifeAlertPlus.API.Services.SimulationManager>();
    await simManager.SeedFromDatabaseAsync(); // Preîncărcăm ultimele date cunoscute din DB în cache-ul din memorie
}
catch (Exception simEx)
{
    var simLogger = app.Services.GetRequiredService<ILogger<Program>>();
    simLogger.LogWarning(simEx, "SimulationManager seeding from DB failed — live data will populate after first ESP POST.");
}

// Must run before any other middleware so that X-Forwarded-Proto / X-Forwarded-For
// from Azure's reverse proxy are honoured. Without this the OAuth middleware generates
// http:// callback URIs that don't match the https:// URI registered in Google Console.
// Procesăm header-ele de la proxy-ul invers (Azure/Nginx) — necesar pentru HTTPS corect
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Must be first: catches unhandled exceptions and re-applies CORS so the browser
// can read the error body instead of seeing an opaque CORS failure on top of a 500.
// Handler global de excepții — interceptează erorile neașteptate și returnează JSON consistent
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        // Re-aplicăm CORS manual pentru ca browser-ul să poată citi mesajul de eroare
        var corsService    = context.RequestServices.GetRequiredService<ICorsService>();
        var policyProvider = context.RequestServices.GetRequiredService<ICorsPolicyProvider>();
        var policy         = await policyProvider.GetPolicyAsync(context, "AllowBlazorClient");
        if (policy is not null)
        {
            var corsResult = corsService.EvaluatePolicy(context, policy);
            corsService.ApplyResult(corsResult, context.Response); // Adăugăm header-ele CORS la răspunsul de eroare
        }

        context.Response.StatusCode  = StatusCodes.Status500InternalServerError; // Cod HTTP 500
        context.Response.ContentType = "application/json";

        // Extragem informații despre excepția apărută
        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        var ex      = feature?.Error;
        var logger  = context.RequestServices.GetService<ILogger<Program>>();
        logger?.LogError(ex, "Unhandled exception"); // Logăm excepția în sistemul de logging

        // Write to SystemError log for the admin ErrorLog page.
        // Scriem eroarea și în baza de date (vizibilă pe pagina de admin)
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

        // Returnăm un mesaj de eroare generic (nu expunem detalii tehnice)
        await context.Response.WriteAsync("{\"success\":false,\"message\":\"An internal server error occurred.\"}");

    });
});

// Middleware de securitate: adăugăm header-e de securitate la fiecare răspuns
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff"; // Previne MIME sniffing
    context.Response.Headers["X-Frame-Options"] = "DENY"; // Previne înglobarea în iframe (clickjacking)
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block"; // Protecție XSS în browsere vechi
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin"; // Controlul header-ului Referer
    await next(); // Continuăm cu următorul middleware
});

// Activăm Swagger doar în mediul de dezvoltare (nu în producție)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(); // Generează documentația JSON a API-ului
    app.UseSwaggerUI(); // Interfața web interactivă pentru testare
}

// Aplicăm politica CORS definită anterior (ordinea contează — trebuie înaintea autentificării)
app.UseCors("AllowBlazorClient");

app.UseResponseCompression(); // Activăm compresia răspunsurilor (Brotli/Gzip)
app.UseRateLimiter(); // Activăm limitarea cererilor
app.UseHttpsRedirection(); // Redirecționăm HTTP → HTTPS

app.UseStaticFiles(); // Servim fișierele statice (imagini, CSS etc.)

// Ordinea corectă: mai întâi verificăm CINE ești, apoi CE poți face
app.UseAuthentication(); // Verifică token-ul JWT și stabilește identitatea utilizatorului
app.UseAuthorization(); // Verifică permisiunile (roluri, politici)

app.MapControllers(); // Înregistrăm toate controller-ele ca endpoint-uri HTTP
// Înregistrăm hub-ul SignalR pentru notificări în timp real
app.MapHub<LifeAlertPlus.API.Hubs.NotificationHub>("/notificationhub");

// Pornim aplicația (blocant — rulează indefinit până la oprire)
app.Run();
