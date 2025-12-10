using System.Net.Http.Json;
using System.Text;

namespace RadarMoves.Client.Services;

/// <summary>
/// Client-side proxy for the server-side ImageCacheService
/// </summary>
public class ImageCacheService {
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImageCacheService> _logger;

    public ImageCacheService(HttpClient httpClient, ILogger<ImageCacheService> logger) {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    /// <summary>
    /// Get cached image as data URL, or null if not cached
    /// </summary>
    public async Task<string?> GetCachedImageAsync(string channel, string timestamp, float elevation) {
        try {
            var url = $"api/ImageCache/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(timestamp)}/{elevation}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode) {
                var result = await response.Content.ReadFromJsonAsync<CacheResponse>();
                if (result?.Cached == true && !string.IsNullOrEmpty(result.DataUrl)) {
                    _logger.LogDebug("Cache hit for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
                    return result.DataUrl;
                }
            }

            _logger.LogDebug("Cache miss for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
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
            var url = $"api/ImageCache/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(timestamp)}/{elevation}";
            var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode) {
                _logger.LogDebug("Cached image for {Channel}/{Timestamp}/{Elevation} ({Size} bytes)",
                    channel, timestamp, elevation, imageBytes.Length);
            } else {
                _logger.LogWarning("Failed to cache image: {StatusCode}", response.StatusCode);
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
            var url = $"api/ImageCache/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(timestamp)}/{elevation}/dataurl";
            var content = new StringContent(dataUrl, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode) {
                _logger.LogDebug("Cached image data URL for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
            } else {
                _logger.LogWarning("Failed to cache image data URL: {StatusCode}", response.StatusCode);
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error caching image data URL for {Channel}/{Timestamp}/{Elevation}", channel, timestamp, elevation);
        }
    }

    /// <summary>
    /// Clear all cached images
    /// </summary>
    public async Task ClearCacheAsync() {
        try {
            var response = await _httpClient.DeleteAsync("api/ImageCache");
            if (response.IsSuccessStatusCode) {
                _logger.LogInformation("Cleared image cache");
            } else {
                _logger.LogWarning("Failed to clear cache: {StatusCode}", response.StatusCode);
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error clearing cache: {Error}", ex.Message);
        }
    }

    private class CacheResponse {
        public bool Cached { get; set; }
        public string? DataUrl { get; set; }
    }
}
