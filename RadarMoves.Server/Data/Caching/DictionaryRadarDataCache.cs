using System.Collections.Concurrent;
using RadarMoves.Server.Data;

namespace RadarMoves.Server.Data.Caching;

/// <summary>
/// In-memory dictionary-based implementation of IRadarDataCache
/// Uses ConcurrentDictionary for thread safety
/// </summary>
public class DictionaryRadarDataCache : IRadarDataCache {
    private readonly ConcurrentDictionary<string, byte[]> _imageCache = new();
    private readonly ConcurrentDictionary<DateTime, ProcessedPVOLMetadata> _pvolCache = new();

    private static string GetImageKey(DateTime timestamp, double elevation, Channel channel) {
        return $"image:{timestamp:yyyyMMddHHmmss}:{elevation}:{channel}";
    }

    public Task<byte[]?> GetImageAsync(DateTime timestamp, double elevation, Channel channel, CancellationToken cancellationToken = default) {
        var key = GetImageKey(timestamp, elevation, channel);
        _imageCache.TryGetValue(key, out var image);
        return Task.FromResult<byte[]?>(image);
    }

    public Task SetImageAsync(DateTime timestamp, double elevation, Channel channel, byte[] imageData, CancellationToken cancellationToken = default) {
        var key = GetImageKey(timestamp, elevation, channel);
        _imageCache[key] = imageData;
        return Task.CompletedTask;
    }

    public Task<ProcessedPVOLMetadata?> GetProcessedPVOLAsync(DateTime timestamp, CancellationToken cancellationToken = default) {
        _pvolCache.TryGetValue(timestamp, out var metadata);
        return Task.FromResult(metadata);
    }

    public Task SetProcessedPVOLAsync(DateTime timestamp, double[] elevations, CancellationToken cancellationToken = default) {
        var metadata = new ProcessedPVOLMetadata {
            Timestamp = timestamp,
            Elevations = elevations,
            ProcessedAt = DateTime.UtcNow
        };
        _pvolCache[timestamp] = metadata;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<DateTime>> GetProcessedPVOLTimestampsAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult<IEnumerable<DateTime>>(_pvolCache.Keys.OrderBy(t => t).ToList());
    }

    public Task<bool> HasImageAsync(DateTime timestamp, double elevation, Channel channel, CancellationToken cancellationToken = default) {
        var key = GetImageKey(timestamp, elevation, channel);
        return Task.FromResult(_imageCache.ContainsKey(key));
    }

    public Task RemovePVOLAsync(DateTime timestamp, CancellationToken cancellationToken = default) {
        // Remove PVOL metadata
        _pvolCache.TryRemove(timestamp, out _);

        // Remove all images for this PVOL
        var keysToRemove = _imageCache.Keys
            .Where(k => k.StartsWith($"image:{timestamp:yyyyMMddHHmmss}:"))
            .ToList();

        foreach (var key in keysToRemove) {
            _imageCache.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}

