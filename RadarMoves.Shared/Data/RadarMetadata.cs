using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadarMoves.Shared.Data;

/// <summary>
/// Base class for radar metadata containing common properties
/// </summary>
public abstract class RadarMetadataBase {
    public DateTime Timestamp { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Metadata for a single polar scan (single elevation angle)
/// </summary>
public class PolarScanMetadata : RadarMetadataBase {
    public double ElevationAngle { get; set; }
    public int NRays { get; set; }
    public int NBins { get; set; }
    public double RScale { get; set; }
    public double RStart { get; set; }

    /// <summary>
    /// Converts to JSON using camelCase naming (API format)
    /// </summary>
    public string ToJson() {
        return JsonSerializer.Serialize(new {
            timestamp = Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            elevationAngle = ElevationAngle,
            nRays = NRays,
            nBins = NBins,
            latitude = Latitude,
            longitude = Longitude,
            height = Height,
            rScale = RScale,
            rStart = RStart
        }, new JsonSerializerOptions {
            WriteIndented = false
        });
    }

    /// <summary>
    /// Converts to a JSON object using camelCase naming (API format)
    /// </summary>
    public object ToJsonObject() {
        return new {
            timestamp = Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            elevationAngle = ElevationAngle,
            nRays = NRays,
            nBins = NBins,
            latitude = Latitude,
            longitude = Longitude,
            height = Height,
            rScale = RScale,
            rStart = RStart
        };
    }
}

/// <summary>
/// Metadata for a polar volume (multiple elevation angles at a single timestamp)
/// </summary>
public class PolarVolumeMetadata : RadarMetadataBase {
    public IReadOnlyList<double> ElevationAngles { get; set; } = Array.Empty<double>();
    public int Count => ElevationAngles.Count;

    /// <summary>
    /// Converts to JSON using camelCase naming (API format)
    /// </summary>
    public string ToJson() {
        return JsonSerializer.Serialize(new {
            timestamp = Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            latitude = Latitude,
            longitude = Longitude,
            height = Height,
            elevationAngles = ElevationAngles,
            count = Count
        }, new JsonSerializerOptions {
            WriteIndented = false
        });
    }

    /// <summary>
    /// Converts to a JSON object using camelCase naming (API format)
    /// </summary>
    public object ToJsonObject() {
        return new {
            timestamp = Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            latitude = Latitude,
            longitude = Longitude,
            height = Height,
            elevationAngles = ElevationAngles,
            count = Count
        };
    }
}

