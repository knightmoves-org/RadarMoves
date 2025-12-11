using System.Collections.Concurrent;
using System.Globalization;
using PureHDF;
using PureHDF.VOL.Native;

namespace RadarMoves.Server.Data;

/// <summary>
/// Implementation of IRadarDataProvider that reads directly from the archive directory
/// </summary>
public class ArchiveRadarDataProvider : IRadarDataProvider {
    private readonly string _archivePath;
    private readonly ILogger<ArchiveRadarDataProvider> _logger;
    private readonly ConcurrentDictionary<DateTime, List<string>> _filesByTimestamp = new();
    private readonly Lazy<Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>>> _dataIndex;

    public ArchiveRadarDataProvider(string archivePath, ILogger<ArchiveRadarDataProvider> logger) {
        _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
        _logger = logger;
        _dataIndex = new Lazy<Task<Dictionary<DateTime, (double[] Angles, string[] FilePaths)>>>(LoadDataIndex);
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
    }

    public IEnumerable<Channel> GetAvailableChannels() => Enum.GetValues<Channel>();

    public async Task<IEnumerable<DateTime>> GetTimestamps() {
        var index = await _dataIndex.Value;
        return index.Keys;
    }




    public async Task<(DateTime Start, DateTime End)?> GetTimeRange() {
        var timestamps = await GetTimestamps();
        var timestampList = timestamps.ToList();
        if (timestampList.Count == 0) return null;
        return (timestampList.Min(), timestampList.Max());
    }

    public async Task<IReadOnlyList<float>?> GetElevationAngles(DateTime timestamp) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }



        var elevations = new List<float>();
        foreach (var filePath in files) {
            try {
                _logger.LogDebug("Reading elevation from {FilePath}", filePath);
                using var scan = new EWRPolarScan(filePath);
                _logger.LogDebug("File {FilePath}: elevation={Elevation}, nRays={NRays}, nBins={NBins}",
                    filePath, scan.ElevationAngle, scan.NRays, scan.NBins);

                // Include all scans, even if they have zero dimensions (might be valid but empty)
                elevations.Add((float)scan.ElevationAngle);
                _logger.LogDebug("Added elevation {Elevation} from {FilePath}", scan.ElevationAngle, filePath);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to read elevation from {FilePath}: {Error}", filePath, ex.Message);
            }
        }

        var uniqueElevations = elevations.Distinct().OrderBy(e => e).ToList();
        _logger.LogInformation("Found {Count} unique elevations for timestamp {Timestamp}: {Elevations}",
            uniqueElevations.Count, timestamp, string.Join(", ", uniqueElevations));
        return uniqueElevations.AsReadOnly();
    }

    public async Task<ScanMetadata?> GetScanMetadata(DateTime timestamp, float elevationAngle) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }



        foreach (var filePath in files) {
            try {
                _logger.LogDebug("Attempting to read file: {FilePath}", filePath);
                using var scan = new EWRPolarScan(filePath);
                var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
                _logger.LogInformation("File {FilePath}: elevation={Elevation}, requested={Requested}, diff={Diff}, nRays={NRays}, nBins={NBins}",
                    filePath, scan.ElevationAngle, elevationAngle, elevationDiff, scan.NRays, scan.NBins);

                if (elevationDiff < 0.5f) { // Increased tolerance for elevation matching
                    if (scan.NRays == 0 || scan.NBins == 0) {
                        _logger.LogWarning("Scan has zero rays or bins: {FilePath}, nRays={NRays}, nBins={NBins} - returning metadata anyway",
                            filePath, scan.NRays, scan.NBins);
                        // Return metadata anyway, even if dimensions are zero
                    } else {
                        _logger.LogInformation("Found matching scan: elevation={Elevation}, nRays={NRays}, nBins={NBins}",
                            scan.ElevationAngle, scan.NRays, scan.NBins);
                    }

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
                } else {
                    _logger.LogDebug("Elevation mismatch: diff={Diff} (threshold=0.5)", elevationDiff);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to read scan from {FilePath}: {Error}", filePath, ex.Message);
            }
        }

        _logger.LogWarning("No matching elevation found for timestamp {Timestamp}, elevation {Elevation}",
            timestamp, elevationAngle);
        return null;
    }

    public async Task<float?> GetValue(Channel channel, DateTime timestamp, float elevationAngle, int ray, int bin) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }

        foreach (var filePath in files) {
            try {
                using var scan = new EWRPolarScan(filePath);
                var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
                _logger.LogDebug("Checking file {FilePath}: elevation={Elevation}, requested={Requested}, diff={Diff}",
                    filePath, scan.ElevationAngle, elevationAngle, elevationDiff);

                if (elevationDiff < 0.5f) { // Increased tolerance for elevation matching
                    if (ray < 0 || ray >= scan.NRays || bin < 0 || bin >= scan.NBins) {
                        _logger.LogWarning("Ray/bin out of range: ray={Ray}/{NRays}, bin={Bin}/{NBins}",
                            ray, scan.NRays, bin, scan.NBins);
                        return null;
                    }
                    var data = scan.Raw[(int)channel];
                    return data[ray, bin];
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to read value from {FilePath}", filePath);
            }
        }

        _logger.LogWarning("No matching elevation found for timestamp {Timestamp}, elevation {Elevation}",
            timestamp, elevationAngle);
        return null;
    }

    public async Task<float?> GetValueByGeo(Channel channel, DateTime timestamp, float elevationAngle, float latitude, float longitude) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) return null;

        foreach (var filePath in files) {
            try {
                using var scan = new EWRPolarScan(filePath);
                var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
                if (elevationDiff < 0.5f) { // Increased tolerance for elevation matching
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
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to read geo value from {FilePath}", filePath);
            }
        }

        return null;
    }

    public async Task<float[,]?> GetRawData(Channel channel, DateTime timestamp, float elevationAngle) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) return null;

        foreach (var filePath in files) {
            try {
                using var scan = new EWRPolarScan(filePath);
                var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
                if (elevationDiff < 0.5f) { // Increased tolerance
                    return scan.GetRawData(channel);
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to read data from {FilePath}", filePath);
            }
        }

        return null;
    }
    public async Task<float[,]?> GetFilteredData(Channel channel, DateTime timestamp, float elevationAngle) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) return null;

        foreach (var filePath in files) {
            try {
                using var scan = new EWRPolarScan(filePath);
                var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
                if (elevationDiff < 0.5f) {
                    return scan.GetFilteredData(channel);
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to read filtered data from {FilePath}", filePath);
            }
        }
        return null;
    }

    public async Task<byte[]?> GetImage(Channel channel, DateTime timestamp, float elevationAngle, int width = 1500, int height = 1500) {
        var files = await GetFilesForTimestamp(timestamp);
        if (files == null || files.Count == 0) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }

        _logger.LogInformation("Generating image for timestamp {Timestamp}, elevation {Elevation}, channel {Channel}, checking {Count} files",
            timestamp, elevationAngle, channel, files.Count);

        // First, try to find a file with matching elevation and non-zero dimensions
        foreach (var filePath in files) {
            try {
                using var scan = new EWRPolarScan(filePath);
                var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
                _logger.LogDebug("Checking file {FilePath}: elevation={Elevation}, requested={Requested}, diff={Diff}, nRays={NRays}, nBins={NBins}",
                    filePath, scan.ElevationAngle, elevationAngle, elevationDiff, scan.NRays, scan.NBins);

                if (elevationDiff < 0.5f) { // Increased tolerance to match GetScanMetadataAsync
                    if (scan.NRays == 0 || scan.NBins == 0) {
                        _logger.LogWarning("Scan has zero rays or bins: {FilePath}, nRays={NRays}, nBins={NBins} - trying next file",
                            filePath, scan.NRays, scan.NBins);
                        continue; // Skip zero-dimension scans for image generation
                    }

                    _logger.LogInformation("Processing scan: nRays={NRays}, nBins={NBins}, lat={Lat}, lon={Lon}",
                        scan.NRays, scan.NBins, scan.Latitude, scan.Longitude);

                    // Get the channel data
                    var channelData = scan[channel];

                    // Create grid spec for interpolation
                    var (lats, lons, _, (latMin, latMax, lonMin, lonMax)) = scan.GetGeodeticCoordinates();
                    var gridSpec = new EWRPolarScan.GridSpec(lonMin, lonMax, latMin, latMax, width, height);

                    _logger.LogInformation("Interpolating with grid spec: width={Width}, height={Height}, lonRange=[{LonMin}, {LonMax}], latRange=[{LatMin}, {LatMax}]",
                        width, height, lonMin, lonMax, latMin, latMax);

                    // Interpolate using IDW
                    var (raster, _, _) = scan.InterpolateIDW(channelData, gridSpec);

                    _logger.LogInformation("Interpolation complete: raster size {Width}x{Height}",
                        raster.GetLength(1), raster.GetLength(0));

                    // Create image writer and get PNG bytes
                    var imageWriter = new ImageWriter(raster);
                    var imageBytes = imageWriter.GetPNGBytes(channel, includeColorBar: true,
                        radarLat: (float)scan.Latitude, radarLon: (float)scan.Longitude, gridSpec: gridSpec);

                    _logger.LogInformation("Image generated: {Size} bytes", imageBytes.Length);
                    return imageBytes;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to generate image from {FilePath}", filePath);
            }
        }

        // If no file with matching elevation and non-zero dimensions found, try any file with non-zero dimensions
        _logger.LogWarning("No matching elevation with data found, trying any file with non-zero dimensions for timestamp {Timestamp}", timestamp);
        foreach (var filePath in files) {
            try {
                using var scan = new EWRPolarScan(filePath);
                if (scan.NRays > 0 && scan.NBins > 0) {
                    _logger.LogInformation("Using file {FilePath} with elevation {Elevation} (requested {Requested})",
                        filePath, scan.ElevationAngle, elevationAngle);

                    // Get the channel data
                    var channelData = scan[channel];

                    // Create grid spec for interpolation
                    var (lats, lons, _, (latMin, latMax, lonMin, lonMax)) = scan.GetGeodeticCoordinates();
                    var gridSpec = new EWRPolarScan.GridSpec(lonMin, lonMax, latMin, latMax, width, height);

                    _logger.LogInformation("Interpolating with grid spec: width={Width}, height={Height}, lonRange=[{LonMin}, {LonMax}], latRange=[{LatMin}, {LatMax}]",
                        width, height, lonMin, lonMax, latMin, latMax);

                    // Interpolate using IDW
                    var (raster, _, _) = scan.InterpolateIDW(channelData, gridSpec);

                    _logger.LogInformation("Interpolation complete: raster size {Width}x{Height}",
                        raster.GetLength(1), raster.GetLength(0));

                    // Create image writer and get PNG bytes
                    var imageWriter = new ImageWriter(raster);
                    var imageBytes = imageWriter.GetPNGBytes(channel, includeColorBar: true,
                        radarLat: (float)scan.Latitude, radarLon: (float)scan.Longitude, gridSpec: gridSpec);

                    _logger.LogInformation("Image generated: {Size} bytes", imageBytes.Length);
                    return imageBytes;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to generate image from {FilePath}", filePath);
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
        // Ensure data index is loaded
        await _dataIndex.Value;

        // Find the PVOL start time (lowest elevation angle for this timestamp)
        // Within 1 second tolerance
        var matchingKey = _filesByTimestamp.Keys.FirstOrDefault(k => Math.Abs((k - timestamp).TotalSeconds) < 1.0);
        if (matchingKey != default) {
            return matchingKey;
        }
        return null;
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
                    _logger.LogDebug("Failed to extract timestamp from file: {FilePath}", filePath);
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

            // Also populate _filesByTimestamp for backward compatibility
            foreach (var (timestamp, entries) in filesByTimestamp) {
                if (!_filesByTimestamp.ContainsKey(timestamp)) {
                    _filesByTimestamp[timestamp] = new List<string>();
                }
                foreach (var (_, filePath) in entries) {
                    if (!_filesByTimestamp[timestamp].Contains(filePath)) {
                        _filesByTimestamp[timestamp].Add(filePath);
                    }
                }
            }

            _logger.LogInformation("Loaded data index with {Count} timestamps (success: {Success}, failed: {Failed})",
                dataIndex.Count, successCount, failCount);
            return dataIndex;
        });
    }

    private async Task<List<string>?> GetFilesForTimestamp(DateTime timestamp) {
        // Ensure data index is loaded (which populates _filesByTimestamp)
        await _dataIndex.Value;

        if (_filesByTimestamp.TryGetValue(timestamp, out var files)) {
            return files;
        }

        // Try to find files with the same timestamp (within a small tolerance)
        var matchingKey = _filesByTimestamp.Keys.FirstOrDefault(k => Math.Abs((k - timestamp).TotalSeconds) < 1.0);
        if (matchingKey != default) {
            return _filesByTimestamp[matchingKey];
        }

        return null;
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

