using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;

namespace RadarMoves.Server.Data;

/// <summary>
/// Dataset that organizes PolarVolumes by timestamp, providing 4D data access:
/// Time (different scans) -> Elevation -> Channel -> NRays x NBins
/// </summary>
public sealed class EWRDataset : IDisposable {
    private readonly Dictionary<DateTime, EWRPolarVolume> _volumes;
    private readonly List<DateTime> _sortedTimestamps;
    private bool _disposed = false;

    /// <summary>
    /// All timestamps in the dataset, sorted chronologically
    /// </summary>
    public ReadOnlyCollection<DateTime> Timestamps { get; private set; } = null!;

    /// <summary>
    /// Number of time points (scans) in the dataset
    /// </summary>
    public int Count => _volumes.Count;

    /// <summary>
    /// Get a PolarVolume by timestamp
    /// </summary>
    public EWRPolarVolume? GetVolume(DateTime timestamp) {
        return _volumes.TryGetValue(timestamp, out var volume) ? volume : null;
    }

    /// <summary>
    /// Get a PolarVolume by index (chronological order)
    /// </summary>
    public EWRPolarVolume? GetVolume(int index) {
        if (index < 0 || index >= _sortedTimestamps.Count) return null;
        return _volumes[_sortedTimestamps[index]];
    }

    /// <summary>
    /// Get a PolarVolume by timestamp
    /// </summary>
    public EWRPolarVolume? this[DateTime timestamp] => GetVolume(timestamp);

    /// <summary>
    /// Get a PolarVolume by index
    /// </summary>
    public EWRPolarVolume? this[int index] => GetVolume(index);

    /// <summary>
    /// Get the nearest volume to a given timestamp
    /// </summary>
    public (EWRPolarVolume volume, DateTime timestamp)? GetNearestVolume(DateTime timestamp) {
        if (_sortedTimestamps.Count == 0) return null;

        // Binary search for nearest timestamp
        int index = _sortedTimestamps.BinarySearch(timestamp);
        if (index >= 0) {
            // Exact match
            return (_volumes[_sortedTimestamps[index]], _sortedTimestamps[index]);
        } else {
            // No exact match, find nearest
            int insertionPoint = ~index;
            if (insertionPoint == 0) {
                return (_volumes[_sortedTimestamps[0]], _sortedTimestamps[0]);
            } else if (insertionPoint >= _sortedTimestamps.Count) {
                return (_volumes[_sortedTimestamps[_sortedTimestamps.Count - 1]], _sortedTimestamps[_sortedTimestamps.Count - 1]);
            } else {
                // Choose the closer of the two adjacent timestamps
                var before = _sortedTimestamps[insertionPoint - 1];
                var after = _sortedTimestamps[insertionPoint];
                var diffBefore = Math.Abs((timestamp - before).TotalSeconds);
                var diffAfter = Math.Abs((after - timestamp).TotalSeconds);
                var nearest = diffBefore <= diffAfter ? before : after;
                return (_volumes[nearest], nearest);
            }
        }
    }

    /// <summary>
    /// Get volumes within a time range
    /// </summary>
    public IEnumerable<(EWRPolarVolume volume, DateTime timestamp)> GetVolumesInRange(DateTime startTime, DateTime endTime) {
        var startIndex = _sortedTimestamps.BinarySearch(startTime);
        if (startIndex < 0) startIndex = ~startIndex;

        for (int i = startIndex; i < _sortedTimestamps.Count; i++) {
            var timestamp = _sortedTimestamps[i];
            if (timestamp > endTime) break;
            yield return (_volumes[timestamp], timestamp);
        }
    }

    /// <summary>
    /// Get 4D data: [time, elevation, channel, ray, bin]
    /// Returns null if any dimension is out of range
    /// </summary>
    public float? GetValue(DateTime timestamp, float elevationAngle, Channel channel, int ray, int bin) {
        var volume = GetVolume(timestamp);
        if (volume == null) return null;

        var scan = volume.GetScanByElevation(elevationAngle);
        if (scan == null) return null;

        if (ray < 0 || ray >= scan.NRays || bin < 0 || bin >= scan.NBins) return null;

        var data = scan.Raw[(int)channel];
        return data[ray, bin];
    }

    /// <summary>
    /// Get 3D data for a specific time and elevation: [channel, ray, bin]
    /// </summary>
    public float[][,]? GetData(DateTime timestamp, float elevationAngle) {
        var volume = GetVolume(timestamp);
        if (volume == null) return null;

        var scan = volume.GetScanByElevation(elevationAngle);
        if (scan == null) return null;

        return scan.Raw;
    }

    /// <summary>
    /// Get 2D data for a specific time, elevation, and channel: [ray, bin]
    /// </summary>
    public float[,]? GetData(DateTime timestamp, float elevationAngle, Channel channel) {
        var volume = GetVolume(timestamp);
        if (volume == null) return null;

        var scan = volume.GetScanByElevation(elevationAngle);
        if (scan == null) return null;

        return scan.GetRawData(channel);
    }

    /// <summary>
    /// Get all available elevation angles for a specific timestamp
    /// </summary>
    public IReadOnlyList<float>? GetElevationAngles(DateTime timestamp) {
        var volume = GetVolume(timestamp);
        if (volume?.ElevationAngles == null) return null;
        // Convert double to float for interface compatibility
        return volume.ElevationAngles.Select(e => (float)e).ToList().AsReadOnly();
    }

    /// <summary>
    /// Get the time range of the dataset
    /// </summary>
    public (DateTime Start, DateTime End)? GetTimeRange() {
        if (_sortedTimestamps.Count == 0) return null;
        return (_sortedTimestamps[0], _sortedTimestamps[_sortedTimestamps.Count - 1]);
    }

    /// <summary>
    /// Get metadata about a specific scan (time, elevation)
    /// </summary>
    public ScanMetadata? GetScanMetadata(DateTime timestamp, float elevationAngle) {
        var volume = GetVolume(timestamp);
        if (volume == null) return null;

        var scan = volume.GetScanByElevation(elevationAngle);
        if (scan == null) return null;

        return new ScanMetadata {
            Timestamp = scan.Datetime,
            ElevationAngle = scan.ElevationAngle,
            NRays = scan.NRays,
            NBins = scan.NBins,
            Latitude = scan.Latitude,
            Longitude = scan.Longitude,
            Height = scan.Height,
            RScale = scan.RScale,
            RStart = scan.RStart
        };
    }

    /// <summary>
    /// Get all available channels
    /// </summary>
    public static IEnumerable<Channel> GetAvailableChannels() => Enum.GetValues<Channel>();

    /// <summary>
    /// Check if a timestamp exists in the dataset
    /// </summary>
    public bool ContainsTimestamp(DateTime timestamp) => _volumes.ContainsKey(timestamp);

    /// <summary>
    /// Indexer: Get value by channel, timestamp, elevation index, ray, bin
    /// </summary>
    public float? this[Channel channel, DateTime timestamp, int scanIndex, int ray, int bin] {
        get {
            var volume = GetVolume(timestamp);
            if (volume == null || scanIndex < 0 || scanIndex >= volume.Count) return null;
            var scan = volume[scanIndex];
            if (ray < 0 || ray >= scan.NRays || bin < 0 || bin >= scan.NBins) return null;
            return scan.Raw[(int)channel][ray, bin];
        }
    }

    /// <summary>
    /// Indexer: Get value by channel, timestamp, elevation angle, ray, bin
    /// </summary>
    public float? this[Channel channel, DateTime timestamp, float elevationAngle, int ray, int bin] {
        get {
            var volume = GetVolume(timestamp);
            if (volume == null) return null;
            var scan = volume.GetScanByElevation(elevationAngle);
            if (scan == null || ray < 0 || ray >= scan.NRays || bin < 0 || bin >= scan.NBins) return null;
            return scan.Raw[(int)channel][ray, bin];
        }
    }

    /// <summary>
    /// Indexer: Get value by channel, timestamp, elevation index, latitude, longitude
    /// Uses geodetic coordinates to find nearest ray/bin
    /// </summary>
    public float? this[Channel channel, DateTime timestamp, int scanIndex, float latitude, float longitude] {
        get {
            var volume = GetVolume(timestamp);
            if (volume == null || scanIndex < 0 || scanIndex >= volume.Count) return null;
            var scan = volume[scanIndex];
            var (lats, lons, _, _) = scan.GetGeodeticCoordinates();

            // Find nearest ray/bin by distance
            double minDist = double.MaxValue;
            int bestRay = -1, bestBin = -1;
            for (int r = 0; r < scan.NRays; r++) {
                for (int b = 0; b < scan.NBins; b++) {
                    double latDiff = lats[r, b] - latitude;
                    double lonDiff = lons[r, b] - longitude;
                    double dist = Math.Sqrt(latDiff * latDiff + lonDiff * lonDiff);
                    if (dist < minDist) {
                        minDist = dist;
                        bestRay = r;
                        bestBin = b;
                    }
                }
            }
            if (bestRay < 0 || bestBin < 0) return null;
            return scan.Raw[(int)channel][bestRay, bestBin];
        }
    }

    /// <summary>
    /// Indexer: Get value by channel, timestamp, elevation angle, latitude, longitude
    /// </summary>
    public float? this[Channel channel, DateTime timestamp, float elevationAngle, float latitude, float longitude] {
        get {
            var volume = GetVolume(timestamp);
            if (volume == null) return null;
            var scan = volume.GetScanByElevation(elevationAngle);
            if (scan == null) return null;
            var (lats, lons, _, _) = scan.GetGeodeticCoordinates();

            double minDist = double.MaxValue;
            int bestRay = -1, bestBin = -1;
            for (int r = 0; r < scan.NRays; r++) {
                for (int b = 0; b < scan.NBins; b++) {
                    double latDiff = lats[r, b] - latitude;
                    double lonDiff = lons[r, b] - longitude;
                    double dist = Math.Sqrt(latDiff * latDiff + lonDiff * lonDiff);
                    if (dist < minDist) {
                        minDist = dist;
                        bestRay = r;
                        bestBin = b;
                    }
                }
            }
            if (bestRay < 0 || bestBin < 0) return null;
            return scan.Raw[(int)channel][bestRay, bestBin];
        }
    }

    /// <summary>
    /// Get data slice with range support for time dimension
    /// </summary>
    public float[][,]? GetDataRange(Channel channel, Range timeRange, int scanIndex, Range? rayRange = null, Range? binRange = null) {
        var (start, length) = timeRange.GetOffsetAndLength(_sortedTimestamps.Count);
        if (start + length > _sortedTimestamps.Count) return null;

        var volumes = new List<EWRPolarVolume>();
        for (int i = start; i < start + length; i++) {
            var vol = GetVolume(i);
            if (vol != null && scanIndex >= 0 && scanIndex < vol.Count) {
                volumes.Add(vol);
            }
        }

        if (volumes.Count == 0) return null;
        var firstScan = volumes[0][scanIndex];
        var nRays = rayRange.HasValue ? rayRange.Value.GetOffsetAndLength(firstScan.NRays).Length : firstScan.NRays;
        var nBins = binRange.HasValue ? binRange.Value.GetOffsetAndLength(firstScan.NBins).Length : firstScan.NBins;

        var result = new float[volumes.Count][,];
        for (int t = 0; t < volumes.Count; t++) {
            var scan = volumes[t][scanIndex];
            var (rayStart, _) = rayRange?.GetOffsetAndLength(scan.NRays) ?? (0, scan.NRays);
            var (binStart, _) = binRange?.GetOffsetAndLength(scan.NBins) ?? (0, scan.NBins);

            result[t] = new float[nRays, nBins];
            for (int r = 0; r < nRays; r++) {
                for (int b = 0; b < nBins; b++) {
                    result[t][r, b] = scan.Raw[(int)channel][rayStart + r, binStart + b];
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Get data slice with range support for elevation dimension
    /// </summary>
    public float[][,]? GetDataRange(Channel channel, DateTime timestamp, Range scanRange, Range? rayRange = null, Range? binRange = null) {
        var volume = GetVolume(timestamp);
        if (volume == null) return null;

        var (start, length) = scanRange.GetOffsetAndLength(volume.Count);
        if (start + length > volume.Count) return null;

        var scans = new List<EWRPolarScan>();
        for (int i = start; i < start + length; i++) {
            scans.Add(volume[i]);
        }

        if (scans.Count == 0) return null;
        var firstScan = scans[0];
        var nRays = rayRange.HasValue ? rayRange.Value.GetOffsetAndLength(firstScan.NRays).Length : firstScan.NRays;
        var nBins = binRange.HasValue ? binRange.Value.GetOffsetAndLength(firstScan.NBins).Length : firstScan.NBins;

        var result = new float[scans.Count][,];
        for (int s = 0; s < scans.Count; s++) {
            var scan = scans[s];
            var (rayStart, _) = rayRange?.GetOffsetAndLength(scan.NRays) ?? (0, scan.NRays);
            var (binStart, _) = binRange?.GetOffsetAndLength(scan.NBins) ?? (0, scan.NBins);

            result[s] = new float[nRays, nBins];
            for (int r = 0; r < nRays; r++) {
                for (int b = 0; b < nBins; b++) {
                    result[s][r, b] = scan.Raw[(int)channel][rayStart + r, binStart + b];
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Create a dataset by scanning a directory for H5 files and grouping by timestamp
    /// </summary>
    public EWRDataset(string directoryPath, bool groupByTimestamp = true) {
        _volumes = new Dictionary<DateTime, EWRPolarVolume>();
        _sortedTimestamps = new List<DateTime>();

        if (!Directory.Exists(directoryPath)) {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var h5Files = Directory.GetFiles(directoryPath, "*.h5", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        if (groupByTimestamp) {
            // Group files by timestamp (assuming files with same timestamp are different elevations)
            var filesByTimestamp = new Dictionary<DateTime, List<string>>();

            foreach (var filePath in h5Files) {
                var timestamp = ExtractTimestampFromFile(filePath);
                if (timestamp.HasValue) {
                    if (!filesByTimestamp.ContainsKey(timestamp.Value)) {
                        filesByTimestamp[timestamp.Value] = new List<string>();
                    }
                    filesByTimestamp[timestamp.Value].Add(filePath);
                }
            }

            // Create PolarVolumes from grouped files
            foreach (var (timestamp, filePaths) in filesByTimestamp) {
                try {
                    var volume = new EWRPolarVolume(filePaths);
                    _volumes[timestamp] = volume;
                    _sortedTimestamps.Add(timestamp);
                } catch (Exception ex) {
                    // Log error but continue processing other files
                    Console.WriteLine($"Error loading volume for timestamp {timestamp}: {ex.Message}");
                }
            }
        } else {
            // Treat each file as a separate volume (single elevation)
            foreach (var filePath in h5Files) {
                var timestamp = ExtractTimestampFromFile(filePath);
                if (timestamp.HasValue) {
                    try {
                        var volume = new EWRPolarVolume(filePath);
                        // Use the actual timestamp from the file if available
                        var actualTimestamp = volume.Timestamp;
                        _volumes[actualTimestamp] = volume;
                        _sortedTimestamps.Add(actualTimestamp);
                    } catch (Exception ex) {
                        Console.WriteLine($"Error loading file {filePath}: {ex.Message}");
                    }
                }
            }
        }

        _sortedTimestamps.Sort();
        Timestamps = _sortedTimestamps.AsReadOnly();
    }

    /// <summary>
    /// Create a dataset from a list of file paths, grouping by timestamp
    /// </summary>
    public EWRDataset(IEnumerable<string> filePaths, bool groupByTimestamp = true) {
        _volumes = new Dictionary<DateTime, EWRPolarVolume>();
        _sortedTimestamps = new List<DateTime>();

        var h5Files = filePaths.Where(f => f.EndsWith(".h5", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (groupByTimestamp) {
            var filesByTimestamp = new Dictionary<DateTime, List<string>>();

            foreach (var filePath in h5Files) {
                var timestamp = ExtractTimestampFromFile(filePath);
                if (timestamp.HasValue) {
                    if (!filesByTimestamp.ContainsKey(timestamp.Value)) {
                        filesByTimestamp[timestamp.Value] = new List<string>();
                    }
                    filesByTimestamp[timestamp.Value].Add(filePath);
                }
            }

            foreach (var (timestamp, paths) in filesByTimestamp) {
                try {
                    var volume = new EWRPolarVolume(paths);
                    _volumes[timestamp] = volume;
                    _sortedTimestamps.Add(timestamp);
                } catch (Exception ex) {
                    Console.WriteLine($"Error loading volume for timestamp {timestamp}: {ex.Message}");
                }
            }
        } else {
            foreach (var filePath in h5Files) {
                var timestamp = ExtractTimestampFromFile(filePath);
                if (timestamp.HasValue) {
                    try {
                        var volume = new EWRPolarVolume(filePath);
                        var actualTimestamp = volume.Timestamp;
                        _volumes[actualTimestamp] = volume;
                        _sortedTimestamps.Add(actualTimestamp);
                    } catch (Exception ex) {
                        Console.WriteLine($"Error loading file {filePath}: {ex.Message}");
                    }
                }
            }
        }

        _sortedTimestamps.Sort();
        Timestamps = _sortedTimestamps.AsReadOnly();
    }

    /// <summary>
    /// Create a dataset from existing PolarVolumes
    /// </summary>
    public EWRDataset(IEnumerable<EWRPolarVolume> volumes) {
        _volumes = new Dictionary<DateTime, EWRPolarVolume>();
        _sortedTimestamps = new List<DateTime>();

        foreach (var volume in volumes) {
            _volumes[volume.Timestamp] = volume;
            _sortedTimestamps.Add(volume.Timestamp);
        }

        _sortedTimestamps.Sort();
        Timestamps = _sortedTimestamps.AsReadOnly();
    }

    /// <summary>
    /// Extract timestamp from EWR file name (e.g., EWR251201160005.h5 -> 2025-12-01 16:00:05)
    /// Format: EWR + YYMMDDHHMMSS
    /// </summary>
    private static DateTime? ExtractTimestampFromFile(string filePath) {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        // Pattern: EWR followed by 12 digits (YYMMDDHHMMSS)
        var match = Regex.Match(fileName, @"EWR(\d{12})");
        if (match.Success) {
            var timestampStr = match.Groups[1].Value;
            // Assume 20YY format (2000-2099)
            if (timestampStr.Length == 12) {
                var year = int.Parse(timestampStr.Substring(0, 2));
                var month = int.Parse(timestampStr.Substring(2, 2));
                var day = int.Parse(timestampStr.Substring(4, 2));
                var hour = int.Parse(timestampStr.Substring(6, 2));
                var minute = int.Parse(timestampStr.Substring(8, 2));
                var second = int.Parse(timestampStr.Substring(10, 2));

                // Handle year 00-99 as 2000-2099
                var fullYear = year < 50 ? 2000 + year : 1900 + year;

                try {
                    return new DateTime(fullYear, month, day, hour, minute, second);
                } catch {
                    return null;
                }
            }
        }

        // Fallback: try to read from file metadata
        try {
            using var scan = new EWRPolarScan(filePath);
            return scan.Datetime;
        } catch {
            return null;
        }
    }

    public void Dispose() {
        if (!_disposed) {
            foreach (var volume in _volumes.Values) {
                volume.Dispose();
            }
            _volumes.Clear();
            _sortedTimestamps.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// Metadata about a radar scan
/// </summary>
public class ScanMetadata {
    public DateTime Timestamp { get; set; }
    public double ElevationAngle { get; set; }
    public int NRays { get; set; }
    public int NBins { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Height { get; set; }
    public double RScale { get; set; }
    public double RStart { get; set; }
}

