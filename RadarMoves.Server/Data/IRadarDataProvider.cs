using RadarMoves.Server.Data;

namespace RadarMoves.Server.Data;

/// <summary>
/// Interface for providing access to radar data
/// </summary>
public interface IRadarDataProvider {
    /// <summary>
    /// Get all available timestamps in the dataset
    /// </summary>
    Task<IEnumerable<DateTime>> GetTimestamps();

    /// <summary>
    /// Get the time range of the dataset
    /// </summary>
    Task<(DateTime Start, DateTime End)?> GetTimeRange();

    /// <summary>
    /// Get elevation angles for a specific timestamp
    /// </summary>
    Task<IReadOnlyList<double>?> GetElevationAngles(DateTime timestamp);

    /// <summary>
    /// Get scan metadata for a specific timestamp and elevation
    /// </summary>
    Task<ScanMetadata?> GetScanMetadata(DateTime timestamp, float elevationAngle);

    /// <summary>
    /// Get data value at specific coordinates (C, T, Z, ray, bin)
    /// </summary>
    Task<float?> GetValue(Channel channel, DateTime timestamp, float elevationAngle, int ray, int bin);

    /// <summary>
    /// Get data value by latitude/longitude (C, T, Z, Y, X)
    /// </summary>
    Task<float?> GetValueByGeo(Channel channel, DateTime timestamp, float elevationAngle, float latitude, float longitude);


    /// <summary>
    /// Get 2D data for a specific channel, time, and elevation (C, T, Z)
    /// </summary>
    Task<float[,]?> GetRawData(Channel channel, DateTime timestamp, float elevationAngle);
    Task<float[,]?> GetFilteredData(Channel channel, DateTime timestamp, float elevationAngle);

    /// <summary>
    /// Get all available channels
    /// </summary>
    IEnumerable<Channel> GetAvailableChannels();

    /// <summary>
    /// Get interpolated image as PNG bytes (C, T, Z)
    /// </summary>
    Task<byte[]?> GetImage(Channel channel, DateTime timestamp, float elevationAngle, int width = 1024, int height = 1024);

    /// <summary>
    /// Get dictionary mapping timestamps to elevation angles and file paths
    /// </summary>
    Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>> GetDataIndex();

    /// <summary>
    /// Get the PVOL start time for a given timestamp (normalizes to PVOL start time)
    /// </summary>
    Task<DateTime?> GetPVOLStartTime(DateTime timestamp);
}

