using RadarMoves.Server.Data;

namespace RadarMoves.Server.Data.Caching;

/// <summary>
/// Interface for caching processed radar data (Geo images, processed PVOLs)
/// </summary>
public interface IRadarDataCache
{
    /// <summary>
    /// Get cached image for a specific timestamp, elevation, and channel
    /// </summary>
    Task<byte[]?> GetImageAsync(DateTime timestamp, float elevation, Channel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache an image for a specific timestamp, elevation, and channel
    /// </summary>
    Task SetImageAsync(DateTime timestamp, float elevation, Channel channel, byte[] imageData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get metadata about a processed PVOL (list of elevations that have been processed)
    /// </summary>
    Task<ProcessedPVOLMetadata?> GetProcessedPVOLAsync(DateTime timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a PVOL as processed with its elevation angles
    /// </summary>
    Task SetProcessedPVOLAsync(DateTime timestamp, float[] elevations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all processed PVOL timestamps
    /// </summary>
    Task<IEnumerable<DateTime>> GetProcessedPVOLTimestampsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a specific image is cached
    /// </summary>
    Task<bool> HasImageAsync(DateTime timestamp, float elevation, Channel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cached data for a specific PVOL
    /// </summary>
    Task RemovePVOLAsync(DateTime timestamp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata about a processed PVOL
/// </summary>
public class ProcessedPVOLMetadata
{
    public DateTime Timestamp { get; set; }
    public float[] Elevations { get; set; } = Array.Empty<float>();
    public DateTime ProcessedAt { get; set; }
}

