using RadarMoves.Server.Components;
using Microsoft.AspNetCore.ResponseCompression;
using StackExchange.Redis;
using RadarMoves.Server.Hubs;
// using RadarMoves.Client.Pages;
// using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ===========================================================================================
// RAZOR COMPONENTS (BLAZOR) CONFIGURATION
// ===========================================================================================

// Register Razor Components services and configure the interactive render modes:
// - AddInteractiveServerComponents: Enables Blazor Server mode (server-side rendering with SignalR)
// - AddInteractiveWebAssemblyComponents: Enables Blazor WebAssembly mode (client-side rendering in browser)
// This hybrid setup allows mixing both render modes in the same application

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

// ===========================================================================================
// SIGNALR AND RESPONSE COMPRESSION CONFIGURATION
// ===========================================================================================

// Register SignalR services - enables real-time bidirectional communication between server and clients
// SignalR is used for the ChatHub and NumberStateHub in this application
builder.Services.AddSignalR();

builder.Services.AddResponseCompression(opts => {
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/octet-stream"]);
});

// ===========================================================================================
// REDIS CONFIGURATION
// ===========================================================================================

// Register Redis connection multiplexer as a singleton (one instance for the entire application lifetime)
// Redis is used for caching, session state, or real-time data distribution
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => {
    // Retrieve the IConfiguration service from the service provider
    var configuration = sp.GetRequiredService<IConfiguration>();

    // Get the Redis connection string from configuration, defaulting to "localhost:6379" if not found
    var connectionString = configuration.GetConnectionString("RedisConnection") ?? "localhost:6379";

    // Create and return a connection multiplexer that manages the connection pool to Redis
    return ConnectionMultiplexer.Connect(connectionString);
});

// ===========================================================================================
// BUILD THE APPLICATION
// ===========================================================================================

// Build the WebApplication instance from the configured builder
// This finalizes the service registration and creates the middleware pipeline
var app = builder.Build();
// Use Response Compression Middleware at the top of the processing pipeline's configuration. Place the following line of code immediately after the line that builds the app (var app = builder.Build();):
app.UseResponseCompression();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.UseWebAssemblyDebugging();
} else {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// Map the StateHub to the application pipeline (before Razor components)
app.MapHub<StateHub>("/stateHub");

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(RadarMoves.Client._Imports).Assembly);

// Map the StateHub to the application pipeline
app.MapHub<StateHub>("/state");

// Start the application
app.Run();
