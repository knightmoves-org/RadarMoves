using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RadarMoves.Client.Services;
using RadarMoves.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register HttpClient if not already registered
builder.Services.AddScoped(sp => {
    var httpClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    return httpClient;
});

// Register image cache service (client-side proxy)
builder.Services.AddScoped<ImageCacheService>();

// Register radar controls service as singleton for shared state across components
builder.Services.AddSingleton<RadarControlsService>();

await builder.Build().RunAsync();
