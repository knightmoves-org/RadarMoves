using RadarMoves.Server.Data;

namespace RadarMoves.Server.Data;

/// <summary>
/// Interface for providing access to radar data
/// </summary>
public interface IRadarDataProvider {
    /// <summary>
    /// Get all available timestamps in the dataset
    /// </summary>
    Task<IEnumerable<DateTime>> GetTimestampsAsync();

    /// <summary>
    /// Get the time range of the dataset
    /// </summary>
    Task<(DateTime Start, DateTime End)?> GetTimeRangeAsync();

    /// <summary>
    /// Get elevation angles for a specific timestamp
    /// </summary>
    Task<IReadOnlyList<float>?> GetElevationAnglesAsync(DateTime timestamp);

    /// <summary>
    /// Get scan metadata for a specific timestamp and elevation
    /// </summary>
    Task<ScanMetadata?> GetScanMetadataAsync(DateTime timestamp, float elevationAngle);

    /// <summary>
    /// Get data value at specific coordinates (C, T, Z, ray, bin)
    /// </summary>
    Task<float?> GetValueAsync(Channel channel, DateTime timestamp, float elevationAngle, int ray, int bin);

    /// <summary>
    /// Get data value by latitude/longitude (C, T, Z, Y, X)
    /// </summary>
    Task<float?> GetValueByGeoAsync(Channel channel, DateTime timestamp, float elevationAngle, float latitude, float longitude);

    /// <summary>
    /// Get 2D data for a specific channel, time, and elevation (C, T, Z)
    /// </summary>
    Task<float[,]?> GetDataAsync(Channel channel, DateTime timestamp, float elevationAngle);

    /// <summary>
    /// Get all available channels
    /// </summary>
    IEnumerable<Channel> GetAvailableChannels();

    /// <summary>
    /// Get interpolated image as PNG bytes (C, T, Z)
    /// </summary>
    Task<byte[]?> GetImageAsync(Channel channel, DateTime timestamp, float elevationAngle, int width = 1500, int height = 1500);
}

