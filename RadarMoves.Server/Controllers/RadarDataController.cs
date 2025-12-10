using Microsoft.AspNetCore.Mvc;
using RadarMoves.Server.Data;
using RadarMoves.Server.Services;
using System.Text.Json;

namespace RadarMoves.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RadarDataController(IRadarDataProvider dataProvider, ILogger<RadarDataController> logger) : ControllerBase {
    private readonly IRadarDataProvider _provider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
    private readonly ILogger<RadarDataController> _logger = logger;

    /// <summary>
    /// Get dataset metadata (time range, available channels)
    /// </summary>
    [HttpGet("metadata")]
    public async Task<IActionResult> GetMetadata() {
        try {
            var timeRange = await _provider.GetTimeRange();
            var timestamps = await _provider.GetTimestamps();
            var timestampList = timestamps.ToList();

            return Ok(new {
                timeRange = timeRange.HasValue ? new { start = timeRange.Value.Start, end = timeRange.Value.End } : null,
                count = timestampList.Count,
                channels = _provider.GetAvailableChannels().Select(c => c.ToString()).ToArray()
            });
        } catch (Exception e) {
            _logger.LogError(e, "Error getting metadata");
            return StatusCode(500, new { error = "Failed to get metadata", message = e.Message });
        }
    }

    /// <summary>
    /// Get all available timestamps
    /// </summary>
    [HttpGet("timestamps")]
    public async Task<IActionResult> GetTimestamps() {
        try {
            var timestamps = await _provider.GetTimestamps();
            return Ok(timestamps.Select(t => t.ToString("yyyy-MM-ddTHH:mm:ss")));
        } catch (Exception e) {
            _logger.LogError(e, "Error getting timestamps");
            return StatusCode(500, new { error = "Failed to get timestamps", message = e.Message });
        }
    }

    /// <summary>
    /// Get elevation angles for a specific timestamp
    /// </summary>
    [HttpGet("elevations/{timestamp}")]
    public async Task<IActionResult> GetElevations(string timestamp) {
        try {
            if (!DateTime.TryParse(timestamp, out var dt)) {
                return BadRequest(new { error = "Invalid timestamp format" });
            }

            var elevations = await _provider.GetElevationAngles(dt);
            if (elevations == null) {
                return NotFound(new { error = "Timestamp not found" });
            }

            return Ok(elevations);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting elevations for timestamp {Timestamp}", timestamp);
            return StatusCode(500, new { error = "Failed to get elevations", message = ex.Message });
        }
    }

    /// <summary>
    /// Get scan metadata
    /// </summary>
    [HttpGet("scan/{timestamp}/{elevation}")]
    public async Task<IActionResult> GetScanMetadata(string timestamp, float elevation) {
        try {
            if (!DateTime.TryParse(timestamp, out var dt)) {
                return BadRequest(new { error = "Invalid timestamp format" });
            }

            var metadata = await _provider.GetScanMetadata(dt, elevation);
            if (metadata == null) {
                return NotFound(new { error = "Scan not found" });
            }

            return Ok(new {
                timestamp = metadata.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
                elevationAngle = metadata.ElevationAngle,
                nRays = metadata.NRays,
                nBins = metadata.NBins,
                latitude = metadata.Latitude,
                longitude = metadata.Longitude,
                height = metadata.Height,
                rScale = metadata.RScale,
                rStart = metadata.RStart
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting scan metadata for timestamp {Timestamp}, elevation {Elevation}", timestamp, elevation);
            return StatusCode(500, new { error = "Failed to get scan metadata", message = ex.Message });
        }
    }

    /// <summary>
    /// Get data value at specific coordinates
    /// </summary>
    [HttpGet("value/{channel}/{timestamp}/{elevation}/{ray}/{bin}")]
    public async Task<IActionResult> GetValue(string channel, string timestamp, float elevation, int ray, int bin) {
        try {
            if (!Enum.TryParse<Channel>(channel, true, out var ch)) {
                return BadRequest(new { error = "Invalid channel" });
            }

            if (!DateTime.TryParse(timestamp, out var dt)) {
                return BadRequest(new { error = "Invalid timestamp format" });
            }

            var value = await _provider.GetValue(ch, dt, elevation, ray, bin);
            if (!value.HasValue) {
                return NotFound(new { error = "Value not found" });
            }

            return Ok(new { value = value.Value });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting value");
            return StatusCode(500, new { error = "Failed to get value", message = ex.Message });
        }
    }

    /// <summary>
    /// Get data value by latitude/longitude
    /// </summary>
    [HttpGet("value-geo/{channel}/{timestamp}/{elevation}/{latitude}/{longitude}")]
    public async Task<IActionResult> GetValueByGeo(string channel, string timestamp, float elevation, float latitude, float longitude) {
        try {
            if (!Enum.TryParse<Channel>(channel, true, out var ch)) {
                return BadRequest(new { error = "Invalid channel" });
            }

            if (!DateTime.TryParse(timestamp, out var dt)) {
                return BadRequest(new { error = "Invalid timestamp format" });
            }

            var value = await _provider.GetValueByGeo(ch, dt, elevation, latitude, longitude);
            if (!value.HasValue) {
                return NotFound(new { error = "Value not found" });
            }

            return Ok(new { value = value.Value });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting geo value");
            return StatusCode(500, new { error = "Failed to get geo value", message = ex.Message });
        }
    }

    /// <summary>
    /// Get interpolated radar image as PNG
    /// </summary>
    [HttpGet("image/{channel}/{timestamp}/{elevation}")]
    [HttpGet("image/{channel}/{timestamp}/{elevation}/{width}/{height}")]
    public async Task<IActionResult> GetImage(string channel, string timestamp, float elevation, int width = 1024, int height = 1024) {
        try {
            if (!Enum.TryParse<Channel>(channel, true, out var ch)) {
                return BadRequest(new { error = "Invalid channel" });
            }

            if (!DateTime.TryParse(timestamp, out var dt)) {
                return BadRequest(new { error = "Invalid timestamp format" });
            }

            var imageBytes = await _provider.GetImage(ch, dt, elevation, width, height);
            if (imageBytes == null || imageBytes.Length == 0) {
                _logger.LogWarning("Image generation failed for channel={Channel}, timestamp={Timestamp}, elevation={Elevation}",
                    ch, dt, elevation);
                return NotFound(new { error = "Image not found", message = "No valid data found for the specified parameters" });
            }

            return File(imageBytes, "image/png");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error generating image");
            return StatusCode(500, new { error = "Failed to generate image", message = ex.Message });
        }
    }

    /// <summary>
    /// Get raw unfiltered data in (nray, nbin) shape as PNG image
    /// </summary>
    [HttpGet("raw/{channel}/{timestamp}/{elevation}")]
    public async Task<IActionResult> GetRawData(string channel, string timestamp, float elevation) {
        try {
            if (!Enum.TryParse<Channel>(channel, true, out var ch)) {
                return BadRequest(new { error = "Invalid channel" });
            }

            if (!DateTime.TryParse(timestamp, out var dt)) {
                return BadRequest(new { error = "Invalid timestamp format" });
            }

            var data = await _provider.GetRawData(ch, dt, elevation);
            if (data == null) {
                return NotFound(new { error = "Data not found" });
            }

            // Create image from raw data using ImageWriter
            var imageWriter = new ImageWriter(data);
            var imageBytes = imageWriter.GetPNGBytes(ch, includeColorBar: true);

            return File(imageBytes, "image/png");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting raw data");
            return StatusCode(500, new { error = "Failed to get raw data", message = ex.Message });
        }
    }
    /// <summary>
    /// Get filtered data in (nray, nbin) shape as PNG image
    /// </summary>
    [HttpGet("filtered/{channel}/{timestamp}/{elevation}")]
    public async Task<IActionResult> GetFilteredData(string channel, string timestamp, float elevation) {
        try {
            if (!Enum.TryParse<Channel>(channel, true, out var ch)) {
                return BadRequest(new { error = "Invalid channel" });
            }

            if (!DateTime.TryParse(timestamp, out var dt)) {
                return BadRequest(new { error = "Invalid timestamp format" });
            }

            var data = await _provider.GetFilteredData(ch, dt, elevation);
            if (data == null) {
                return NotFound(new { error = "Data not found" });
            }

            // Create image from filtered data using ImageWriter
            var imageWriter = new ImageWriter(data);
            var imageBytes = imageWriter.GetPNGBytes(ch, includeColorBar: true);

            return File(imageBytes, "image/png");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting filtered data");
            return StatusCode(500, new { error = "Failed to get filtered data", message = ex.Message });
        }
    }
}

