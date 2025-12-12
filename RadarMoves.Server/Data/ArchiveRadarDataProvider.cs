using System.Globalization;
using PureHDF;
using PureHDF.VOL.Native;
using RadarMoves.Shared.Data;

namespace RadarMoves.Server.Data;

/// <summary>
/// Implementation of IRadarDataProvider that reads directly from the archive directory
/// </summary>
public class ArchiveRadarDataProvider : IRadarDataProvider {
    private readonly string _archivePath;
    private readonly ILogger<ArchiveRadarDataProvider> _logger;
    private readonly Lazy<Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>>> _dataIndex;
    private readonly int _defaultImageResolution;

    public ArchiveRadarDataProvider(string archivePath, ILogger<ArchiveRadarDataProvider> logger, IConfiguration? configuration = null) {
        _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
        _logger = logger;
        _dataIndex = new Lazy<Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>>>(LoadDataIndex);
        _defaultImageResolution = configuration?.GetValue<int>("RadarData:ImageResolution") ?? 1024;
    }
    private static string? GetArchivePath(string? configPath) {
        string? archivePath = null;
        if (!string.IsNullOrEmpty(configPath)) {
            if (Path.IsPathRooted(configPath)) {
                archivePath = configPath;
            } else {
                archivePath ??= Path.Combine(Directory.GetCurrentDirectory(), "..", configPath);
            }
        }

        return archivePath;
    }
    public ArchiveRadarDataProvider(IConfiguration configuration, ILogger<ArchiveRadarDataProvider> logger) {
        _archivePath = GetArchivePath(configuration["RadarData:Path"]) ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _dataIndex = new Lazy<Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>>>(LoadDataIndex);
        _defaultImageResolution = configuration.GetValue<int>("RadarData:ImageResolution", 1024);
    }

    public IEnumerable<Channel> GetAvailableChannels() => Enum.GetValues<Channel>();

    public async Task<IEnumerable<DateTime>> GetTimestamps() {
        var index = await _dataIndex.Value;
        return index.Keys;
    }

    public async Task<(DateTime Start, DateTime End)?> GetTimeRange() {
        var index = await _dataIndex.Value;
        if (index.Count == 0) return null;
        return (index.Keys.Min(), index.Keys.Max());
    }

    public async Task<IReadOnlyList<double>?> GetElevationAngles(DateTime timestamp) {
        var index = await _dataIndex.Value;
        var matchingKey = FindMatchingTimestamp(index, timestamp);
        if (matchingKey == default || !index.TryGetValue(matchingKey, out var data)) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }
        return data.Angles;
    }

    public async Task<PolarScanMetadata?> GetScanMetadata(DateTime timestamp, float elevationAngle) {
        var filePath = await FindScanByElevation(timestamp, elevationAngle);
        if (filePath == null) {
            _logger.LogWarning("No matching elevation found for timestamp {Timestamp}, elevation {Elevation}",
                timestamp, elevationAngle);
            return null;
        }

        using var scan = new EWRPolarScan(filePath);
        return new PolarScanMetadata {
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

    public async Task<float?> GetValue(Channel channel, DateTime timestamp, float elevationAngle, int ray, int bin) {
        var filePath = await FindScanByElevation(timestamp, elevationAngle);
        if (filePath == null) {
            _logger.LogWarning("No matching elevation found for timestamp {Timestamp}, elevation {Elevation}",
                timestamp, elevationAngle);
            return null;
        }

        using var scan = new EWRPolarScan(filePath);
        if (ray < 0 || ray >= scan.NRays || bin < 0 || bin >= scan.NBins) {
            return null;
        }
        var data = scan.Raw[(int)channel];
        return data[ray, bin];
    }

    public async Task<float?> GetValueByGeo(Channel channel, DateTime timestamp, float elevationAngle, float latitude, float longitude) {
        var filePath = await FindScanByElevation(timestamp, elevationAngle);
        if (filePath == null) return null;

        using var scan = new EWRPolarScan(filePath);
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
        var data = scan.Raw[(int)channel];
        return data[bestRay, bestBin];
    }

    public async Task<float[,]?> GetRawData(Channel channel, DateTime timestamp, float elevationAngle) {
        var filePath = await FindScanByElevation(timestamp, elevationAngle);
        if (filePath == null) return null;

        using var scan = new EWRPolarScan(filePath);
        return scan.GetRawData(channel);
    }

    public async Task<float[,]?> GetFilteredData(Channel channel, DateTime timestamp, float elevationAngle) {
        var filePath = await FindScanByElevation(timestamp, elevationAngle);
        if (filePath == null) return null;

        using var scan = new EWRPolarScan(filePath);
        return scan.GetFilteredData(channel);
    }

    public async Task<byte[]?> GetImage(Channel channel, DateTime timestamp, float elevationAngle, int width = 0, int height = 0) {
        // Use configured default if not specified
        if (width == 0) width = _defaultImageResolution;
        if (height == 0) height = _defaultImageResolution;
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }

        // First, try to find a file with matching elevation and non-zero dimensions
        var filePath = await FindScanByElevation(timestamp, elevationAngle, requireValidDimensions: true);
        if (filePath != null) {
            using var scan = new EWRPolarScan(filePath);
            return GenerateImage(scan, channel, width, height);
        }

        // If no file with matching elevation and non-zero dimensions found, try any file with non-zero dimensions
        foreach (var fallbackPath in files) {
            using var scan = new EWRPolarScan(fallbackPath);
            if (scan.NRays > 0 && scan.NBins > 0) {
                return GenerateImage(scan, channel, width, height);
            }
        }

        _logger.LogWarning("No files with valid data found for image generation: timestamp {Timestamp}, elevation {Elevation}",
            timestamp, elevationAngle);
        return null;
    }

    public async Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>> GetDataIndex() {
        return await _dataIndex.Value;
    }

    public async Task<DateTime?> GetPVOLStartTime(DateTime timestamp) {
        var index = await _dataIndex.Value;
        var matchingKey = FindMatchingTimestamp(index, timestamp);
        return matchingKey != default ? matchingKey : null;
    }

    private async Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>> LoadDataIndex() {
        return await Task.Run(() => {
            if (!Directory.Exists(_archivePath)) {
                _logger.LogError("Archive directory not found: {ArchivePath}", _archivePath);
                _logger.LogError("Current working directory: {CurrentDir}", Directory.GetCurrentDirectory());
                _logger.LogError("Trying absolute path: {AbsolutePath}", Path.GetFullPath(_archivePath));
                return new Dictionary<DateTime, (double[] Angles, string[] FilePaths)>();
            }

            var h5Files = Directory.GetFiles(_archivePath, "*.h5", SearchOption.TopDirectoryOnly);
            _logger.LogInformation("Found {Count} H5 files in {ArchivePath}", h5Files.Length, _archivePath);

            if (h5Files.Length == 0) {
                _logger.LogWarning("No H5 files found in directory: {ArchivePath}", _archivePath);
                return new Dictionary<DateTime, (double[] Angles, string[] FilePaths)>();
            }

            // Group files by timestamp
            var filesByTimestamp = new Dictionary<DateTime, List<(double elevation, string filePath)>>();
            int successCount = 0;
            int failCount = 0;

            foreach (var filePath in h5Files) {
                var timestamp = ExtractTimestampFromFile(filePath);
                if (timestamp.HasValue) {
                    try {
                        using var scan = new EWRPolarScan(filePath);

                        // Skip files with invalid dimensions (empty/corrupted scans)
                        if (scan.NRays == 0 || scan.NBins == 0) {
                            _logger.LogWarning("Skipping file with invalid dimensions: {FilePath} (NRays={NRays}, NBins={NBins})",
                                filePath, scan.NRays, scan.NBins);
                            failCount++;
                            continue;
                        }

                        // Convert float elevation to double for precise matching
                        var elevationDouble = (double)scan.ElevationAngle;

                        if (!filesByTimestamp.ContainsKey(timestamp.Value)) {
                            filesByTimestamp[timestamp.Value] = new List<(double, string)>();
                        }

                        filesByTimestamp[timestamp.Value].Add((elevationDouble, filePath));
                        successCount++;
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Failed to read elevation from {FilePath} for data index", filePath);
                        failCount++;
                    }
                } else {
                    failCount++;
                }
            }

            // Build the final dictionary structure
            var dataIndex = new Dictionary<DateTime, (double[] Angles, string[] FilePaths)>();
            foreach (var (timestamp, entries) in filesByTimestamp) {
                // Sort by elevation angle
                var sortedEntries = entries.OrderBy(e => e.elevation).ToList();
                var angles = sortedEntries.Select(e => e.elevation).ToArray();
                var filePaths = sortedEntries.Select(e => e.filePath).ToArray();
                dataIndex[timestamp] = (angles, filePaths);
            }

            _logger.LogInformation("Loaded data index with {Count} timestamps (success: {Success}, failed: {Failed})",
                dataIndex.Count, successCount, failCount);
            return dataIndex;
        });
    }

    private async Task<List<string>?> GetFilesForTimestamp(DateTime timestamp) {
        var index = await _dataIndex.Value;
        var matchingKey = FindMatchingTimestamp(index, timestamp);
        if (matchingKey != default && index.TryGetValue(matchingKey, out var data)) {
            return data.FilePaths.ToList();
        }
        return null;
    }

    private static DateTime FindMatchingTimestamp(Dictionary<DateTime, (double[] Angles, string[] FilePaths)> index, DateTime timestamp) {
        if (index.TryGetValue(timestamp, out _)) {
            return timestamp;
        }
        return index.Keys.FirstOrDefault(k => Math.Abs((k - timestamp).TotalSeconds) < 1.0);
    }

    private async Task<string?> FindScanByElevation(DateTime timestamp, float elevationAngle, bool requireValidDimensions = false) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) return null;

        foreach (var filePath in files) {
            using var scan = new EWRPolarScan(filePath);
            var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
            if (elevationDiff < 0.5f) {
                if (requireValidDimensions && (scan.NRays == 0 || scan.NBins == 0)) {
                    continue;
                }
                return filePath;
            }
        }
        return null;
    }

    private static byte[] GenerateImage(EWRPolarScan scan, Channel channel, int width, int height) {
        var channelData = scan[channel];
        var (_, _, _, (latMin, latMax, lonMin, lonMax)) = scan.GetGeodeticCoordinates();
        var gridSpec = new EWRPolarScan.GridSpec(lonMin, lonMax, latMin, latMax, width, height);
        var (raster, _, _) = scan.InterpolateIDW(channelData, gridSpec);

        var imageWriter = new ImageWriter(raster);
        return imageWriter.GetPNGBytes(channel, includeColorBar: true,
            radarLat: (float)scan.Latitude, radarLon: (float)scan.Longitude, gridSpec: gridSpec);
    }

    private static DateTime? ExtractTimestampFromFile(string filePath) {
        try {
            // Use PureHDF to read the top-level "what" group and extract date/time attributes
            // This gives us the PVOL start time, not the file creation time
            using var file = H5File.OpenRead(filePath);
            var what = file.Group("what");

            string date = what.GetAttribute<string>("date");
            string time = what.GetAttribute<string>("time");

            // Parse the date and time attributes (format: yyyyMMddHHmmss)
            return DateTime.ParseExact($"{date}{time}", "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        } catch (Exception) {
            // If we can't read from H5 file, return null
            return null;
        }
    }
}

