using Microsoft.AspNetCore.SignalR;
using RadarMoves.Server.Data;
using RadarMoves.Server.Data.Caching;
using RadarMoves.Server.Data.Indexing;
using RadarMoves.Server.Hubs;

namespace RadarMoves.Server.Services;

/// <summary>
/// Background service that processes PVOLs (Polar Volumes) and caches Geo images.
/// Processes existing PVOLs on startup, watches for new files, and processes on-demand.
/// </summary>
public class PVOLProcessingService : BackgroundService
{
    private readonly IRadarDataProvider _dataProvider;
    private readonly IRadarDataCache _cache;
    private readonly IHubContext<RadarDataHub> _hubContext;
    private readonly ILogger<PVOLProcessingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly FileSystemWatcher? _fileWatcher;
    private readonly string _archivePath;
    private readonly HashSet<DateTime> _processingPVOLs = new();
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1); // Process one PVOL at a time

    public PVOLProcessingService(
        IRadarDataProvider dataProvider,
        IRadarDataCache cache,
        IHubContext<RadarDataHub> hubContext,
        ILogger<PVOLProcessingService> logger,
        IConfiguration configuration)
    {
        _dataProvider = dataProvider;
        _cache = cache;
        _hubContext = hubContext;
        _logger = logger;
        _configuration = configuration;

        // Get archive path from configuration
        var configPath = configuration["RadarData:Path"];
        if (!string.IsNullOrEmpty(configPath))
        {
            if (Path.IsPathRooted(configPath))
            {
                _archivePath = configPath;
            }
            else
            {
                var possibleBases = new[]
                {
                    Directory.GetCurrentDirectory(),
                    AppDomain.CurrentDomain.BaseDirectory,
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                    "/home/leaver/RadarMoves"
                };

                foreach (var baseDir in possibleBases)
                {
                    if (!string.IsNullOrEmpty(baseDir))
                    {
                        var testPath = Path.Combine(baseDir, configPath);
                        if (Directory.Exists(testPath))
                        {
                            _archivePath = testPath;
                            break;
                        }
                    }
                }

                _archivePath ??= Path.Combine(Directory.GetCurrentDirectory(), configPath);
            }
        }
        else
        {
            var possiblePaths = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "data", "ewr", "samples"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ewr", "samples"),
                "/home/leaver/RadarMoves/data/ewr/samples"
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    _archivePath = path;
                    break;
                }
            }

            _archivePath ??= Path.Combine(Directory.GetCurrentDirectory(), "data", "ewr", "samples");
        }

        // Setup file watcher if enabled
        var watchDirectory = _configuration.GetValue<bool>("RadarData:Processing:WatchDirectory", true);
        if (watchDirectory && Directory.Exists(_archivePath))
        {
            _fileWatcher = new FileSystemWatcher(_archivePath, "*.h5")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = false // Will be enabled in ExecuteAsync
            };

            _fileWatcher.Created += OnFileCreated;
            _fileWatcher.Changed += OnFileChanged;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PVOLProcessingService starting");

        // Process existing PVOLs on startup if enabled
        var processOnStartup = _configuration.GetValue<bool>("RadarData:Processing:ProcessOnStartup", true);
        if (processOnStartup)
        {
            await ProcessAllExistingPVOLsAsync(stoppingToken);
        }

        // Enable file watcher if configured
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = true;
            _logger.LogInformation("File watcher enabled for {Path}", _archivePath);
        }

        // Keep service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
        }

        _logger.LogInformation("PVOLProcessingService stopping");
    }

    private async Task ProcessAllExistingPVOLsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing all existing PVOLs");

        try
        {
            var series = await _dataProvider.GetSeries();
            var pvols = series.Index
                .Cast<(DateTime, float)>()
                .GroupBy(idx => idx.Item1)
                .ToList();

            _logger.LogInformation("Found {Count} PVOLs to process", pvols.Count);

            foreach (var pvolGroup in pvols)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var timestamp = pvolGroup.Key;
                await ProcessPVOLAsync(timestamp, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing existing PVOLs");
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("New file created: {FilePath}", e.FullPath);
        _ = Task.Run(async () =>
        {
            // Wait a bit for file to be fully written
            await Task.Delay(TimeSpan.FromSeconds(2));
            await ProcessNewFileAsync(e.FullPath);
        });
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("File changed: {FilePath}", e.FullPath);
        // File changed events can be noisy, so we mainly rely on Created events
    }

    private async Task ProcessNewFileAsync(string filePath)
    {
        try
        {
            // Extract timestamp from file
            using var scan = new EWRPolarScan(filePath);
            var timestamp = scan.Datetime;

            // Normalize to PVOL start time
            var pvolStartTime = await _dataProvider.GetPVOLStartTime(timestamp);
            if (pvolStartTime.HasValue)
            {
                await ProcessPVOLAsync(pvolStartTime.Value, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing new file {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Process a specific PVOL (process all elevation angles in ascending order)
    /// </summary>
    public async Task ProcessPVOLAsync(DateTime pvolStartTime, CancellationToken cancellationToken)
    {
        await _processingSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Check if already processing
            if (_processingPVOLs.Contains(pvolStartTime))
            {
                _logger.LogDebug("PVOL {Timestamp} is already being processed", pvolStartTime);
                return;
            }

            _processingPVOLs.Add(pvolStartTime);
            _logger.LogInformation("Processing PVOL {Timestamp}", pvolStartTime);

            try
            {
                // Get all elevations for this PVOL
                var elevations = await _dataProvider.GetElevationAngles(pvolStartTime);
                if (elevations == null || elevations.Count == 0)
                {
                    _logger.LogWarning("No elevations found for PVOL {Timestamp}", pvolStartTime);
                    return;
                }

                var processedElevations = new List<float>();

                // Process each elevation angle in ascending order
                foreach (var elevation in elevations.OrderBy(e => e))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    _logger.LogInformation("Processing elevation {Elevation} for PVOL {Timestamp}", elevation, pvolStartTime);

                    // Process all channels for this elevation
                    foreach (var channel in _dataProvider.GetAvailableChannels())
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        // Check if already cached
                        var hasImage = await _cache.HasImageAsync(pvolStartTime, elevation, channel, cancellationToken);
                        if (hasImage)
                        {
                            _logger.LogDebug("Image already cached for {Timestamp}, {Elevation}, {Channel}", pvolStartTime, elevation, channel);
                            continue;
                        }

                        // Generate and cache image
                        try
                        {
                            var imageBytes = await _dataProvider.GetImage(channel, pvolStartTime, elevation, 1024, 1024);
                            if (imageBytes != null && imageBytes.Length > 0)
                            {
                                await _cache.SetImageAsync(pvolStartTime, elevation, channel, imageBytes, cancellationToken);
                                _logger.LogInformation("Cached image for {Timestamp}, {Elevation}, {Channel} ({Size} bytes)",
                                    pvolStartTime, elevation, channel, imageBytes.Length);

                                // Stream image to clients
                                await _hubContext.Clients.All.SendAsync("ImageAvailable", pvolStartTime, elevation, channel.ToString(), imageBytes, cancellationToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error generating image for {Timestamp}, {Elevation}, {Channel}",
                                pvolStartTime, elevation, channel);
                        }
                    }

                    processedElevations.Add(elevation);
                }

                // Mark PVOL as processed
                await _cache.SetProcessedPVOLAsync(pvolStartTime, processedElevations.ToArray(), cancellationToken);

                // Notify clients that PVOL is complete
                await _hubContext.Clients.All.SendAsync("PVOLProcessed", pvolStartTime, processedElevations.ToArray(), cancellationToken);

                _logger.LogInformation("Completed processing PVOL {Timestamp} with {Count} elevations",
                    pvolStartTime, processedElevations.Count);
            }
            finally
            {
                _processingPVOLs.Remove(pvolStartTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PVOL {Timestamp}", pvolStartTime);
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }
}

