using StackExchange.Redis;
using System.Text.Json;
using RadarMoves.Server.Data;

namespace RadarMoves.Server.Data.Caching;

/// <summary>
/// Redis-backed implementation of IRadarDataCache
/// Uses IConnectionMultiplexer for Redis operations
/// </summary>
public class RedisRadarDataCache(IConnectionMultiplexer multiplexer, ILogger<RedisRadarDataCache>? logger = null, TimeSpan? defaultTtl = null) : IRadarDataCache {
    private readonly IDatabase _database = multiplexer.GetDatabase();
    private readonly ILogger<RedisRadarDataCache>? _logger = logger;
    private readonly TimeSpan _defaultTtl = defaultTtl ?? TimeSpan.FromHours(24);

    private const string ImageKeyPrefix = "radar:image:";
    private const string PVOLKeyPrefix = "radar:pvol:";
    private const string PVOLListKey = "radar:pvols:list";

    private static string GetImageKey(DateTime timestamp, double elevation, Channel channel) {
        return $"{ImageKeyPrefix}{timestamp:yyyyMMddHHmmss}:{elevation}:{channel}";
    }

    private static string GetPVOLKey(DateTime timestamp) {
        return $"{PVOLKeyPrefix}{timestamp:yyyyMMddHHmmss}";
    }

    public async Task<byte[]?> GetImageAsync(DateTime timestamp, double elevation, Channel channel, CancellationToken cancellationToken = default) {
        var key = GetImageKey(timestamp, elevation, channel);
        var value = await _database.StringGetAsync(key);
        return value.HasValue ? (byte[]?)value : null;
    }

    public async Task SetImageAsync(DateTime timestamp, double elevation, Channel channel, byte[] imageData, CancellationToken cancellationToken = default) {
        var key = GetImageKey(timestamp, elevation, channel);
        await _database.StringSetAsync(key, imageData, _defaultTtl);
    }


    public async Task<ProcessedPVOLMetadata?> GetProcessedPVOLAsync(DateTime timestamp, CancellationToken cancellationToken = default) {
        var key = GetPVOLKey(timestamp);
        var value = await _database.StringGetAsync(key);

        if (!value.HasValue) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<ProcessedPVOLMetadata>(value!);
        } catch (JsonException ex) {
            _logger?.LogError(ex, "Failed to deserialize PVOL metadata for timestamp {Timestamp}", timestamp);
            return null;
        }
    }

    public async Task SetProcessedPVOLAsync(DateTime timestamp, double[] elevations, CancellationToken cancellationToken = default) {
        var metadata = new ProcessedPVOLMetadata {
            Timestamp = timestamp,
            Elevations = elevations,
            ProcessedAt = DateTime.UtcNow
        };

        var key = GetPVOLKey(timestamp);
        var json = JsonSerializer.Serialize(metadata);

        await _database.StringSetAsync(key, json, _defaultTtl);

        // Add to sorted set for efficient retrieval of all PVOLs
        await _database.SortedSetAddAsync(PVOLListKey, timestamp.ToString("yyyyMMddHHmmss"), timestamp.Ticks);
    }

    public async Task<IEnumerable<DateTime>> GetProcessedPVOLTimestampsAsync(CancellationToken cancellationToken = default) {
        var values = await _database.SortedSetRangeByScoreAsync(PVOLListKey);
        var timestamps = values
            .Select(v => v.ToString())
            .Where(s => s.Length == 14)
            .Select(s => DateTime.ParseExact(s, "yyyyMMddHHmmss", null))
            .OrderBy(t => t)
            .ToList();

        return timestamps;
    }

    public async Task<bool> HasImageAsync(DateTime timestamp, double elevation, Channel channel, CancellationToken cancellationToken = default) {
        var key = GetImageKey(timestamp, elevation, channel);
        return await _database.KeyExistsAsync(key);
    }

    public async Task RemovePVOLAsync(DateTime timestamp, CancellationToken cancellationToken = default) {
        // Remove PVOL metadata
        var pvolKey = GetPVOLKey(timestamp);
        await _database.KeyDeleteAsync(pvolKey);

        // Remove from sorted set
        await _database.SortedSetRemoveAsync(PVOLListKey, timestamp.ToString("yyyyMMddHHmmss"));

        // Find and remove all images for this PVOL
        var pattern = $"{ImageKeyPrefix}{timestamp:yyyyMMddHHmmss}:*";
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern).ToArray();

        if (keys.Length > 0) {
            await _database.KeyDeleteAsync(keys);
        }
    }
}

