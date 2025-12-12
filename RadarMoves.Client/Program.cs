using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RadarMoves.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register HttpClient if not already registered
builder.Services.AddScoped(sp => {
    var httpClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    return httpClient;
});

// Register radar controls service as singleton for shared state across components
builder.Services.AddSingleton<RadarControlsService>();

await builder.Build().RunAsync();
