using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Http;

namespace RadarMoves.Server.Services;

/// <summary>
/// Service for caching radar images using ASP.NET Core session state (works in API controllers)
/// Falls back to ProtectedSessionStorage when available (for Blazor components)
/// Based on: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state?view=aspnetcore-10.0
/// </summary>
public class ImageCacheService {
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ProtectedSessionStorage? _protectedSessionStorage;
    private readonly ILogger<ImageCacheService> _logger;
    private const string CachePrefix = "radar_image_";
    private const string CacheIndexKey = "radar_image_index";
    private const int MaxCacheSize = 50; // Maximum number of images to cache
    private const long MaxCacheSizeBytes = 10 * 1024 * 1024; // 10MB limit (approximate)

    public ImageCacheService(
        IHttpContextAccessor httpContextAccessor,
        ProtectedSessionStorage? protectedSessionStorage,
        ILogger<ImageCacheService> logger) {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _protectedSessionStorage = protectedSessionStorage;
        _logger = logger;
    }

    /// <summary>
    /// Generate a cache key from image parameters
    /// </summary>
    private static string GetCacheKey(string channel, string timestamp, float elevation) {
        // Use invariant culture for elevation to ensure consistent formatting
        var elevationStr = elevation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{CachePrefix}{channel}_{timestamp}_{elevationStr}";
    }

    /// <summary>
    /// Get the current HTTP session (works in API controllers after UseSession middleware)
    /// </summary>
    private ISession? GetSession() {
        return _httpContextAccessor.HttpContext?.Session;
    }

    /// <summary>
    /// Get cached image as data URL, or null if not cached
    /// </summary>
    public async Task<string?> GetCachedImageAsync(string channel, string timestamp, float elevation) {
        var cacheKey = GetCacheKey(channel, timestamp, elevation);

        // Try ASP.NET Core session state first (works in API controllers)
        var session = GetSession();
        if (session != null) {
            try {
                // Load session data asynchronously for better performance
                await session.LoadAsync();

                var cachedDataUrl = session.GetString(cacheKey);
                if (!string.IsNullOrEmpty(cachedDataUrl)) {
                    _logger.LogDebug("Cache hit (session) for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                    return cachedDataUrl;
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Error reading from session cache for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
            }
        }

        // Try ProtectedSessionStorage if available (for Blazor components)
        if (_protectedSessionStorage != null) {
            try {
                var result = await _protectedSessionStorage.GetAsync<string>(cacheKey);
                if (result.Success && !string.IsNullOrEmpty(result.Value)) {
                    _logger.LogDebug("Cache hit (protected session) for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                    // Also store in ASP.NET Core session for faster access
                    if (session != null) {
                        try {
                            await session.LoadAsync();
                            session.SetString(cacheKey, result.Value);
                            await session.CommitAsync();
                        } catch {
                            // Ignore if session commit fails
                        }
                    }
                    return result.Value;
                }
            } catch (InvalidOperationException) {
                // JavaScript interop not available - expected in some contexts
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Error reading from protected session cache for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
            }
        }

        _logger.LogDebug("Cache miss for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        return null;
    }

    /// <summary>
    /// Store image in cache as base64 data URL
    /// </summary>
    public async Task CacheImageAsync(string channel, string timestamp, float elevation, byte[] imageBytes) {
        try {
            // Convert to base64 data URL
            var base64 = Convert.ToBase64String(imageBytes);
            var dataUrl = $"data:image/png;base64,{base64}";

            var cacheKey = GetCacheKey(channel, timestamp, elevation);

            // Check cache size before adding
            await EnsureCacheSizeAsync(dataUrl.Length);

            // Store in ASP.NET Core session state (works in API controllers)
            var session = GetSession();
            if (session != null) {
                try {
                    await session.LoadAsync();
                    session.SetString(cacheKey, dataUrl);

                    // Update cache index
                    await UpdateSessionCacheIndexAsync(session, cacheKey, add: true);

                    // Store metadata
                    var metadataKey = $"{cacheKey}_meta";
                    var metadata = new CacheMetadata {
                        Timestamp = DateTime.UtcNow,
                        SizeBytes = dataUrl.Length
                    };
                    var metadataJson = JsonSerializer.Serialize(metadata);
                    session.SetString(metadataKey, metadataJson);

                    await session.CommitAsync();
                    _logger.LogDebug("Cached image in session for {Channel}/{Timestamp}/{Elevation} ({Size} bytes)",
                        channel, timestamp, elevation, dataUrl.Length);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Error caching image in session for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                }
            }

            // Also store in ProtectedSessionStorage if available (for Blazor components)
            if (_protectedSessionStorage != null) {
                try {
                    await _protectedSessionStorage.SetAsync(cacheKey, dataUrl);
                    var metadataKey = $"{cacheKey}_meta";
                    var metadata = new CacheMetadata {
                        Timestamp = DateTime.UtcNow,
                        SizeBytes = dataUrl.Length
                    };
                    await _protectedSessionStorage.SetAsync(metadataKey, metadata);
                    await UpdateProtectedSessionCacheIndexAsync(cacheKey, add: true);
                } catch (InvalidOperationException) {
                    // JavaScript interop not available - expected in some contexts
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Error caching image in protected session for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                }
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error caching image for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        }
    }

    /// <summary>
    /// Store image in cache from a data URL (for when image is already loaded)
    /// </summary>
    public async Task CacheImageDataUrlAsync(string channel, string timestamp, float elevation, string dataUrl) {
        try {
            var cacheKey = GetCacheKey(channel, timestamp, elevation);

            // Check cache size before adding
            await EnsureCacheSizeAsync(dataUrl.Length);

            // Store in ASP.NET Core session state (works in API controllers)
            var session = GetSession();
            if (session != null) {
                try {
                    await session.LoadAsync();
                    session.SetString(cacheKey, dataUrl);

                    // Update cache index
                    await UpdateSessionCacheIndexAsync(session, cacheKey, add: true);

                    // Store metadata
                    var metadataKey = $"{cacheKey}_meta";
                    var metadata = new CacheMetadata {
                        Timestamp = DateTime.UtcNow,
                        SizeBytes = dataUrl.Length
                    };
                    var metadataJson = JsonSerializer.Serialize(metadata);
                    session.SetString(metadataKey, metadataJson);

                    await session.CommitAsync();
                    _logger.LogDebug("Cached image data URL in session for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Error caching image data URL in session for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                }
            }

            // Also store in ProtectedSessionStorage if available (for Blazor components)
            if (_protectedSessionStorage != null) {
                try {
                    await _protectedSessionStorage.SetAsync(cacheKey, dataUrl);
                    var metadataKey = $"{cacheKey}_meta";
                    var metadata = new CacheMetadata {
                        Timestamp = DateTime.UtcNow,
                        SizeBytes = dataUrl.Length
                    };
                    await _protectedSessionStorage.SetAsync(metadataKey, metadata);
                    await UpdateProtectedSessionCacheIndexAsync(cacheKey, add: true);
                } catch (InvalidOperationException) {
                    // JavaScript interop not available - expected in some contexts
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Error caching image data URL in protected session for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                }
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error caching image data URL for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        }
    }

    /// <summary>
    /// Ensure cache doesn't exceed size limits by removing oldest entries
    /// </summary>
    private async Task EnsureCacheSizeAsync(int newItemSize) {
        var session = GetSession();
        if (session == null) return;

        try {
            await session.LoadAsync();

            // Get cache index
            var indexJson = session.GetString(CacheIndexKey);
            var cacheKeys = string.IsNullOrEmpty(indexJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(indexJson) ?? new List<string>();

            if (cacheKeys.Count == 0)
                return;

            // Get metadata for all cached items
            var items = new List<CacheItem>();
            foreach (var key in cacheKeys) {
                var metadataKey = $"{key}_meta";
                var metadataJson = session.GetString(metadataKey);
                if (!string.IsNullOrEmpty(metadataJson)) {
                    var metadata = JsonSerializer.Deserialize<CacheMetadata>(metadataJson);
                    if (metadata != null) {
                        items.Add(new CacheItem { Key = key, MetadataKey = metadataKey, Metadata = metadata });
                    }
                }
            }

            // Calculate total size
            var totalSize = items.Sum(i => i.Metadata.SizeBytes) + newItemSize;

            // If we're over the count limit, remove oldest
            if (items.Count >= MaxCacheSize) {
                var sorted = items.OrderBy(i => i.Metadata.Timestamp).ToList();
                var toRemove = sorted.Take(items.Count - MaxCacheSize + 1);
                foreach (var item in toRemove) {
                    session.Remove(item.Key);
                    session.Remove(item.MetadataKey);
                    cacheKeys.Remove(item.Key);
                    totalSize -= item.Metadata.SizeBytes;
                }
            }

            // If we're over the size limit, remove oldest until under limit
            if (totalSize > MaxCacheSizeBytes) {
                var sorted = items.OrderBy(i => i.Metadata.Timestamp).ToList();
                foreach (var item in sorted) {
                    if (totalSize <= MaxCacheSizeBytes)
                        break;

                    session.Remove(item.Key);
                    session.Remove(item.MetadataKey);
                    cacheKeys.Remove(item.Key);
                    totalSize -= item.Metadata.SizeBytes;
                }
            }

            // Update the index
            var updatedIndexJson = JsonSerializer.Serialize(cacheKeys);
            session.SetString(CacheIndexKey, updatedIndexJson);
            await session.CommitAsync();
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error managing cache size: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Clear all cached images
    /// </summary>
    public async Task ClearCacheAsync() {
        var session = GetSession();

        // Clear ASP.NET Core session cache
        if (session != null) {
            try {
                await session.LoadAsync();

                var indexJson = session.GetString(CacheIndexKey);
                var cacheKeys = string.IsNullOrEmpty(indexJson)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(indexJson) ?? new List<string>();

                foreach (var key in cacheKeys) {
                    session.Remove(key);
                    session.Remove($"{key}_meta");
                }

                session.Remove(CacheIndexKey);
                await session.CommitAsync();
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Error clearing session cache: {Error}", ex.Message);
            }
        }

        // Clear ProtectedSessionStorage cache if available
        if (_protectedSessionStorage != null) {
            try {
                var indexKey = $"{CachePrefix}_index";
                var indexResult = await _protectedSessionStorage.GetAsync<List<string>>(indexKey);
                var cacheKeys = indexResult.Success ? indexResult.Value ?? new List<string>() : new List<string>();

                foreach (var key in cacheKeys) {
                    await _protectedSessionStorage.DeleteAsync(key);
                    await _protectedSessionStorage.DeleteAsync($"{key}_meta");
                }

                await _protectedSessionStorage.DeleteAsync(indexKey);
            } catch (InvalidOperationException) {
                // JavaScript interop not available - expected in some contexts
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Error clearing protected session cache: {Error}", ex.Message);
            }
        }

        _logger.LogInformation("Cleared image cache");
    }

    /// <summary>
    /// Update the cache index in ASP.NET Core session
    /// </summary>
    private async Task UpdateSessionCacheIndexAsync(ISession session, string cacheKey, bool add) {
        try {
            var indexJson = session.GetString(CacheIndexKey);
            var cacheKeys = string.IsNullOrEmpty(indexJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(indexJson) ?? new List<string>();

            if (add && !cacheKeys.Contains(cacheKey)) {
                cacheKeys.Add(cacheKey);
            } else if (!add && cacheKeys.Contains(cacheKey)) {
                cacheKeys.Remove(cacheKey);
            }

            var updatedIndexJson = JsonSerializer.Serialize(cacheKeys);
            session.SetString(CacheIndexKey, updatedIndexJson);
            await session.CommitAsync();
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error updating session cache index: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Update the cache index in ProtectedSessionStorage
    /// </summary>
    private async Task UpdateProtectedSessionCacheIndexAsync(string cacheKey, bool add) {
        if (_protectedSessionStorage == null) return;

        try {
            var indexKey = $"{CachePrefix}_index";
            var indexResult = await _protectedSessionStorage.GetAsync<List<string>>(indexKey);
            var cacheKeys = indexResult.Success ? indexResult.Value ?? new List<string>() : new List<string>();

            if (add && !cacheKeys.Contains(cacheKey)) {
                cacheKeys.Add(cacheKey);
            } else if (!add && cacheKeys.Contains(cacheKey)) {
                cacheKeys.Remove(cacheKey);
            }

            await _protectedSessionStorage.SetAsync(indexKey, cacheKeys);
        } catch (InvalidOperationException) {
            // JavaScript interop not available - expected in some contexts
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error updating protected session cache index: {Error}", ex.Message);
        }
    }

    [Serializable]
    private class CacheMetadata {
        public DateTime Timestamp { get; set; }
        public int SizeBytes { get; set; }
    }

    private class CacheItem {
        public string Key { get; set; } = string.Empty;
        public string MetadataKey { get; set; } = string.Empty;
        public CacheMetadata Metadata { get; set; } = null!;
    }
}
