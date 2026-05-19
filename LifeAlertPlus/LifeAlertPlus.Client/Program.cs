using LifeAlertPlus.Client;
using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = (builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress).TrimEnd('/');

builder.Services.AddScoped<TokenAuthorizationHandler>();

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<TokenAuthorizationHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});

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
builder.Services.AddScoped<ImportApiClient>();
builder.Services.AddScoped<MeasurementApiClient>();
builder.Services.AddScoped<AIPredictionService>();
builder.Services.AddScoped<ChatbotClientService>();

builder.Services.AddSingleton<LanguageService>();
builder.Services.AddScoped<PushNotificationClientService>(sp =>
    new PushNotificationClientService(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>()));

await builder.Build().RunAsync();
