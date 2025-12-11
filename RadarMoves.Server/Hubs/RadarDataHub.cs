using Microsoft.AspNetCore.SignalR;
using RadarMoves.Server.Data;
using RadarMoves.Server.Data.Caching;
using RadarMoves.Server.Services;

namespace RadarMoves.Server.Hubs;

/// <summary>
/// SignalR hub for real-time radar data streaming
/// </summary>
public class RadarDataHub(
    IRadarDataProvider dataProvider,
    IRadarDataCache cache,
    PVOLProcessingService processingService,
    ILogger<RadarDataHub> logger) : Hub {
    private readonly IRadarDataProvider _dataProvider = dataProvider;
    private readonly IRadarDataCache _cache = cache;
    private readonly PVOLProcessingService _processingService = processingService;
    private readonly ILogger<RadarDataHub> _logger = logger;

    /// <summary>
    /// Client subscribes to PVOL update notifications
    /// </summary>
    public async Task SubscribeToPVOLUpdates() {
        await Groups.AddToGroupAsync(Context.ConnectionId, "PVOLUpdates");
        _logger.LogInformation("Client {ConnectionId} subscribed to PVOL updates", Context.ConnectionId);
    }

    /// <summary>
    /// Get list of all processed PVOL timestamps
    /// </summary>
    public async Task<IEnumerable<DateTime>> GetAvailablePVOLs() {
        try {
            var timestamps = await _cache.GetProcessedPVOLTimestampsAsync();
            return timestamps;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting available PVOLs");
            return [];
        }
    }

    /// <summary>
    /// Request processing of a specific PVOL
    /// </summary>
    public async Task RequestPVOL(DateTime timestamp) {
        try {
            _logger.LogInformation("Client {ConnectionId} requested PVOL {Timestamp}", Context.ConnectionId, timestamp);
            await _processingService.ProcessPVOLAsync(timestamp, CancellationToken.None);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing PVOL {Timestamp} requested by {ConnectionId}", timestamp, Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", $"Failed to process PVOL: {ex.Message}");
        }
    }

    /// <summary>
    /// Get cached image for a specific timestamp, elevation, and channel
    /// </summary>
    public async Task<byte[]?> GetCachedImage(DateTime timestamp, float elevation, string channel) {
        try {
            if (!Enum.TryParse<Channel>(channel, true, out var channelEnum)) {
                _logger.LogWarning("Invalid channel: {Channel}", channel);
                return null;
            }

            var image = await _cache.GetImageAsync(timestamp, (double)elevation, channelEnum);
            return image;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting cached image for {Timestamp}, {Elevation}, {Channel}",
                timestamp, elevation, channel);
            return null;
        }
    }

    /// <summary>
    /// Get PVOL metadata (elevations)
    /// </summary>
    public async Task<object?> GetPVOLMetadata(DateTime timestamp) {
        try {
            var metadata = await _cache.GetProcessedPVOLAsync(timestamp);
            if (metadata == null) {
                return null;
            }

            return new {
                timestamp = metadata.Timestamp,
                elevations = metadata.Elevations,
                processedAt = metadata.ProcessedAt
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting PVOL metadata for {Timestamp}", timestamp);
            return null;
        }
    }

    /// <summary>
    /// Get data index structure
    /// </summary>
    public async Task<object?> GetMultiIndex() {
        try {
            var dataIndex = await _dataProvider.GetDataIndex();

            // Flatten for entries list
            var entries = new List<object>();
            foreach (var (timestamp, (angles, filePaths)) in dataIndex) {
                for (int i = 0; i < angles.Length; i++) {
                    entries.Add(new {
                        timestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        elevation = angles[i],
                        filePath = filePaths[i]
                    });
                }
            }

            return new {
                count = entries.Count,
                entries = entries,
                groupedByTime = dataIndex
                    .Select(kvp => new {
                        timestamp = kvp.Key.ToString("yyyy-MM-dd HH:mm:ss"),
                        elevations = kvp.Value.Angles,
                        filePaths = kvp.Value.FilePaths,
                        count = kvp.Value.Angles.Length
                    })
                    .OrderBy(g => g.timestamp)
                    .ToList()
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting data index");
            return null;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception) {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "PVOLUpdates");
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

