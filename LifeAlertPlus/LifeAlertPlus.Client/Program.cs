using LifeAlertPlus.Client;
using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Punctul de start al aplicației Blazor WebAssembly — configurează componentele rădăcină,
// HttpClient-ul autentificat și înregistrează toate serviciile în containerul DI
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// URL-ul de bază al API-ului backend — citit din configurație, cu fallback la adresa host-ului curent
var apiBaseUrl = (builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress).TrimEnd('/');

builder.Services.AddScoped<TokenAuthorizationHandler>();

// HttpClient implicit folosit de toți clienții API tipizați de mai jos — atașează handler-ul
// care injectează token-ul JWT (Bearer) pe fiecare request și fixează adresa de bază a API-ului
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<TokenAuthorizationHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});

// Înregistrarea clienților API tipizați și a serviciilor de stare/business logic ale clientului,
// toate scoped (o instanță per "sesiune" de circuit/WASM, recreate la reload)
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<UserApiClient>();
builder.Services.AddScoped<MonitoredApiClient>();
builder.Services.AddScoped<UserMonitoredApiClient>();
builder.Services.AddScoped<SimulationService>();
builder.Services.AddScoped<SimulationBackgroundService>();
builder.Services.AddScoped<TokenParserService>();
builder.Services.AddScoped<ProfilePictureService>();
builder.Services.AddScoped<UserStateService>();
builder.Services.AddScoped<MeasurementApiClient>();
builder.Services.AddScoped<WifiApiClient>();
builder.Services.AddScoped<AIPredictionService>();
builder.Services.AddScoped<AdminApiClient>();
builder.Services.AddScoped<ChatbotClientService>();

// LanguageService e singleton — starea limbii curente trebuie să fie globală pentru toată aplicația
builder.Services.AddSingleton<LanguageService>();
// PushNotificationClientService e construit manual pentru a-i pasa explicit configurația și IJSRuntime
builder.Services.AddScoped<PushNotificationClientService>(sp =>
    new PushNotificationClientService(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>()));

await builder.Build().RunAsync();
