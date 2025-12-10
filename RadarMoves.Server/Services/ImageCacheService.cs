using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Text.Json;

namespace RadarMoves.Server.Services;

/// <summary>
/// Service for caching radar images in browser session storage
/// </summary>
public class ImageCacheService {
    private readonly ProtectedSessionStorage _protectedSessionStorage;
    private readonly ILogger<ImageCacheService> _logger;
    private const string CachePrefix = "radar_image_";
    private const int MaxCacheSize = 50; // Maximum number of images to cache
    private const long MaxCacheSizeBytes = 10 * 1024 * 1024; // 10MB limit (approximate)

    public ImageCacheService(ProtectedSessionStorage protectedSessionStorage, ILogger<ImageCacheService> logger) {
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
    /// Get cached image as data URL, or null if not cached
    /// </summary>
    public async Task<string?> GetCachedImageAsync(string channel, string timestamp, float elevation) {
        try {
            var cacheKey = GetCacheKey(channel, timestamp, elevation);
            var result = await _protectedSessionStorage.GetAsync<string>(cacheKey);

            if (result.Success && !string.IsNullOrEmpty(result.Value)) {
                _logger.LogDebug("Cache hit for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                return result.Value;
            }

            _logger.LogDebug("Cache miss for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
            return null;
        } catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop") || ex.Message.Contains("statically rendered")) {
            // JavaScript interop not available during SSR or in API controllers - this is expected
            _logger.LogDebug("Cache unavailable (SSR/API context) for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
            return null;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error reading from cache for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
            return null;
        }
    }

    /// <summary>
    /// Store image in cache as base64 data URL
    /// </summary>
    public async Task CacheImageAsync(string channel, string timestamp, float elevation, byte[] imageBytes) {
        try {
            // Convert to base64 data URL
            var base64 = Convert.ToBase64String(imageBytes);
            var dataUrl = $"data:image/png;base64,{base64}";

            // Check cache size before adding
            await EnsureCacheSizeAsync(dataUrl.Length);

            var cacheKey = GetCacheKey(channel, timestamp, elevation);
            await _protectedSessionStorage.SetAsync(cacheKey, dataUrl);

            // Store metadata for cache management
            var metadataKey = $"{cacheKey}_meta";
            var metadata = new CacheMetadata {
                Timestamp = DateTime.UtcNow,
                SizeBytes = dataUrl.Length
            };
            await _protectedSessionStorage.SetAsync(metadataKey, metadata);

            // Update cache index
            await UpdateCacheIndexAsync(cacheKey, add: true);

            _logger.LogDebug("Cached image for {Channel}/{Timestamp}/{Elevation} ({Size} bytes)",
                channel, timestamp, elevation, dataUrl.Length);
        } catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop") || ex.Message.Contains("statically rendered")) {
            // JavaScript interop not available during SSR or in API controllers - this is expected
            _logger.LogDebug("Cache unavailable (SSR/API context) for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error caching image for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        }
    }

    /// <summary>
    /// Store image in cache from a data URL (for when image is already loaded)
    /// </summary>
    public async Task CacheImageDataUrlAsync(string channel, string timestamp, float elevation, string dataUrl) {
        try {
            // Check cache size before adding
            await EnsureCacheSizeAsync(dataUrl.Length);

            var cacheKey = GetCacheKey(channel, timestamp, elevation);
            await _protectedSessionStorage.SetAsync(cacheKey, dataUrl);

            // Store metadata for cache management
            var metadataKey = $"{cacheKey}_meta";
            var metadata = new CacheMetadata {
                Timestamp = DateTime.UtcNow,
                SizeBytes = dataUrl.Length
            };
            await _protectedSessionStorage.SetAsync(metadataKey, metadata);

            // Update cache index
            await UpdateCacheIndexAsync(cacheKey, add: true);

            _logger.LogDebug("Cached image data URL for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        } catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop") || ex.Message.Contains("statically rendered")) {
            // JavaScript interop not available during SSR or in API controllers - this is expected
            _logger.LogDebug("Cache unavailable (SSR/API context) for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error caching image data URL for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        }
    }

    /// <summary>
    /// Ensure cache doesn't exceed size limits by removing oldest entries
    /// </summary>
    private async Task EnsureCacheSizeAsync(int newItemSize) {
        try {
            // Note: ProtectedSessionStorage doesn't provide a way to enumerate all keys
            // We'll track cache entries in a separate index key
            var indexKey = $"{CachePrefix}_index";
            var indexResult = await _protectedSessionStorage.GetAsync<List<string>>(indexKey);
            var cacheKeys = indexResult.Success ? indexResult.Value ?? new List<string>() : new List<string>();

            if (cacheKeys.Count == 0)
                return;

            // Get metadata for all cached items
            var items = new List<CacheItem>();
            foreach (var key in cacheKeys) {
                var metadataKey = $"{key}_meta";
                var metadataResult = await _protectedSessionStorage.GetAsync<CacheMetadata>(metadataKey);
                if (metadataResult.Success && metadataResult.Value != null) {
                    items.Add(new CacheItem { Key = key, MetadataKey = metadataKey, Metadata = metadataResult.Value });
                }
            }

            // Calculate total size
            var totalSize = items.Sum(i => i.Metadata.SizeBytes) + newItemSize;

            // If we're over the count limit, remove oldest
            if (items.Count >= MaxCacheSize) {
                var sorted = items.OrderBy(i => i.Metadata.Timestamp).ToList();
                var toRemove = sorted.Take(items.Count - MaxCacheSize + 1);
                foreach (var item in toRemove) {
                    await _protectedSessionStorage.DeleteAsync(item.Key);
                    await _protectedSessionStorage.DeleteAsync(item.MetadataKey);
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

                    await _protectedSessionStorage.DeleteAsync(item.Key);
                    await _protectedSessionStorage.DeleteAsync(item.MetadataKey);
                    cacheKeys.Remove(item.Key);
                    totalSize -= item.Metadata.SizeBytes;
                }
            }

            // Update the index
            await _protectedSessionStorage.SetAsync(indexKey, cacheKeys);
        } catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop") || ex.Message.Contains("statically rendered")) {
            // JavaScript interop not available during SSR or in API controllers - this is expected
            // Silently return - cache management will be skipped
            return;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error managing cache size: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Clear all cached images
    /// </summary>
    public async Task ClearCacheAsync() {
        try {
            var indexKey = $"{CachePrefix}_index";
            var indexResult = await _protectedSessionStorage.GetAsync<List<string>>(indexKey);
            var cacheKeys = indexResult.Success ? indexResult.Value ?? new List<string>() : new List<string>();

            foreach (var key in cacheKeys) {
                await _protectedSessionStorage.DeleteAsync(key);
                await _protectedSessionStorage.DeleteAsync($"{key}_meta");
            }

            await _protectedSessionStorage.DeleteAsync(indexKey);
            _logger.LogInformation("Cleared image cache");
        } catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop") || ex.Message.Contains("statically rendered")) {
            // JavaScript interop not available during SSR or in API controllers - this is expected
            _logger.LogDebug("Cache unavailable (SSR/API context) - cannot clear cache");
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error clearing cache: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Update the cache index to track all cached keys
    /// </summary>
    private async Task UpdateCacheIndexAsync(string cacheKey, bool add) {
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
        } catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop") || ex.Message.Contains("statically rendered")) {
            // JavaScript interop not available during SSR or in API controllers - this is expected
            // Silently return - index update will be skipped
            return;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error updating cache index: {Error}", ex.Message);
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

