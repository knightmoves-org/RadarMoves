using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using StackExchange.Redis;

using RadarMoves.Server.Components;
using RadarMoves.Server.Services;
using RadarMoves.Server.Hubs;
using RadarMoves.Server.Data;
using RadarMoves.Server.Data.Caching;

var builder = WebApplication.CreateBuilder(args);

// ===========================================================================================
// COMMAND-LINE ARGUMENT PROCESSING
// ===========================================================================================

// Check for --demo flag and override data path if present
// Usage: dotnet run --project RadarMoves.Server -- --demo
// Or when running the compiled executable: ./RadarMoves.Server --demo
if (args.Contains("--demo")) {
    builder.Configuration["RadarData:Path"] = "data/ewr/archive";
    Console.WriteLine("Demo mode enabled: Using data/ewr/archive");
}

// ===========================================================================================
// RAZOR COMPONENTS (BLAZOR) CONFIGURATION
// ===========================================================================================

// Register Razor Components services and configure the interactive render modes:
// - AddInteractiveServerComponents: Enables Blazor Server mode (server-side rendering with SignalR)
// - AddInteractiveWebAssemblyComponents: Enables Blazor WebAssembly mode (client-side rendering in browser)
// This hybrid setup allows mixing both render modes in the same application

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddInteractiveServerComponents(); // Needed for ProtectedBrowserStorage

// Register ProtectedSessionStorage for secure browser storage
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedSessionStorage>();

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
// RADAR DATASET SERVICE
// ===========================================================================================

// Register the radar data provider (reads directly from archive)
// Singleton because it's the same for the entire application lifetime
builder.Services.AddSingleton<IRadarDataProvider>(sp => {
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<ArchiveRadarDataProvider>>();
    return new ArchiveRadarDataProvider(configuration, logger);
});

builder.Services.AddSingleton<RadarDatasetService>();
builder.Services.AddScoped<RadarMoves.Server.Services.ImageCacheService>();
builder.Services.AddScoped<RadarMoves.Client.Services.ImageCacheService>();
builder.Services.AddControllers();

// Register HttpClient for Blazor WebAssembly components during server-side rendering
// This is needed because InteractiveWebAssembly components are initially rendered on the server
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpClient>(sp => {
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var httpContext = httpContextAccessor.HttpContext;
    if (httpContext != null) {
        var request = httpContext.Request;
        var baseAddress = $"{request.Scheme}://{request.Host}";
        return new HttpClient { BaseAddress = new Uri(baseAddress) };
    }
    return new HttpClient { BaseAddress = new Uri("https://localhost") };
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
// RADAR DATA CACHING
// ===========================================================================================

// Register radar data cache based on configuration
builder.Services.AddSingleton<IRadarDataCache>(sp => {
    var configuration = sp.GetRequiredService<IConfiguration>();
    var cacheType = configuration["RadarData:CacheType"] ?? "Dictionary";
    var logger = sp.GetRequiredService<ILogger<IRadarDataCache>>();

    if (cacheType.Equals("Redis", StringComparison.OrdinalIgnoreCase)) {
        var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
        var redisLogger = sp.GetRequiredService<ILogger<RedisRadarDataCache>>();
        return new RedisRadarDataCache(multiplexer, redisLogger);
    } else {
        return new DictionaryRadarDataCache();
    }
});

// ===========================================================================================
// BACKGROUND PROCESSING SERVICE
// ===========================================================================================

// Register PVOL processing service - needs to be both a hosted service and accessible for injection
builder.Services.AddSingleton<PVOLProcessingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PVOLProcessingService>());

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

app.MapControllers();

app.MapStaticAssets();

// Map the StateHub to the application pipeline (before Razor components)
app.MapHub<StateHub>("/stateHub");

// Map the RadarDataHub for real-time radar data streaming
app.MapHub<RadarDataHub>("/radarDataHub");

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(RadarMoves.Client._Imports).Assembly);

// Map the StateHub to the application pipeline
// app.MapHub<StateHub>("/state");

// Start the application
app.Run();
