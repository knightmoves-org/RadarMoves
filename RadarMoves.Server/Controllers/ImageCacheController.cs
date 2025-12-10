using Microsoft.AspNetCore.Mvc;
using RadarMoves.Server.Services;

namespace RadarMoves.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImageCacheController : ControllerBase {
    private readonly ImageCacheService _imageCacheService;
    private readonly ILogger<ImageCacheController> _logger;

    public ImageCacheController(ImageCacheService imageCacheService, ILogger<ImageCacheController> logger) {
        _imageCacheService = imageCacheService ?? throw new ArgumentNullException(nameof(imageCacheService));
        _logger = logger;
    }

    /// <summary>
    /// Get cached image as data URL, or null if not cached
    /// </summary>
    [HttpGet("{channel}/{timestamp}/{elevation}")]
    public async Task<IActionResult> GetCachedImage(string channel, string timestamp, float elevation) {
        try {
            var cachedImage = await _imageCacheService.GetCachedImageAsync(channel, timestamp, elevation);
            if (cachedImage == null) {
                return NotFound(new { cached = false });
            }
            return Ok(new { cached = true, dataUrl = cachedImage });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting cached image");
            return StatusCode(500, new { error = "Failed to get cached image", message = ex.Message });
        }
    }

    /// <summary>
    /// Cache image from byte array
    /// </summary>
    [HttpPost("{channel}/{timestamp}/{elevation}")]
    [Consumes("application/octet-stream")]
    public async Task<IActionResult> CacheImage(string channel, string timestamp, float elevation) {
        try {
            using var memoryStream = new MemoryStream();
            await Request.Body.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            await _imageCacheService.CacheImageAsync(channel, timestamp, elevation, imageBytes);
            return Ok(new { success = true });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error caching image");
            return StatusCode(500, new { error = "Failed to cache image", message = ex.Message });
        }
    }

    /// <summary>
    /// Cache image from data URL
    /// </summary>
    [HttpPost("{channel}/{timestamp}/{elevation}/dataurl")]
    public async Task<IActionResult> CacheImageDataUrl(string channel, string timestamp, float elevation, [FromBody] string dataUrl) {
        try {
            await _imageCacheService.CacheImageDataUrlAsync(channel, timestamp, elevation, dataUrl);
            return Ok(new { success = true });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error caching image data URL");
            return StatusCode(500, new { error = "Failed to cache image data URL", message = ex.Message });
        }
    }

    /// <summary>
    /// Clear all cached images
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> ClearCache() {
        try {
            await _imageCacheService.ClearCacheAsync();
            return Ok(new { success = true });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error clearing cache");
            return StatusCode(500, new { error = "Failed to clear cache", message = ex.Message });
        }
    }
}

