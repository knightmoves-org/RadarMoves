using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RadarMoves.Server.Data;

/// <summary>
/// Implementation of IRadarDataProvider that reads directly from the archive directory
/// </summary>
public class ArchiveRadarDataProvider : IRadarDataProvider {
    private readonly string _archivePath;
    private readonly ILogger<ArchiveRadarDataProvider> _logger;
    private readonly ConcurrentDictionary<DateTime, List<string>> _filesByTimestamp = new();
    private readonly Lazy<Task<List<DateTime>>> _timestamps;

    public ArchiveRadarDataProvider(string archivePath, ILogger<ArchiveRadarDataProvider> logger) {
        _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
        _logger = logger;
        _timestamps = new Lazy<Task<List<DateTime>>>(LoadTimestampsAsync);
    }

    public ArchiveRadarDataProvider(IConfiguration configuration, ILogger<ArchiveRadarDataProvider> logger) {
        var configPath = configuration["RadarData:Path"];
        string? archivePath = null;

        if (!string.IsNullOrEmpty(configPath)) {
            if (Path.IsPathRooted(configPath)) {
                archivePath = configPath;
            } else {
                // Try multiple possible base directories
                var possibleBases = new[] {
                    Directory.GetCurrentDirectory(),
                    AppDomain.CurrentDomain.BaseDirectory,
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                    "/home/leaver/RadarMoves" // Fallback for known location
                };

                foreach (var baseDir in possibleBases) {
                    if (!string.IsNullOrEmpty(baseDir)) {
                        var testPath = Path.Combine(baseDir, configPath);
                        if (Directory.Exists(testPath)) {
                            archivePath = testPath;
                            break;
                        }
                    }
                }

                // If none found, use current directory
                if (archivePath == null) {
                    archivePath = Path.Combine(Directory.GetCurrentDirectory(), configPath);
                }
            }
        } else {
            // Default path - try multiple locations
            var possiblePaths = new[] {
                Path.Combine(Directory.GetCurrentDirectory(), "data", "ewr", "archive"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ewr", "archive"),
                "/home/leaver/RadarMoves/data/ewr/archive"
            };

            foreach (var path in possiblePaths) {
                if (Directory.Exists(path)) {
                    archivePath = path;
                    break;
                }
            }

            if (archivePath == null) {
                archivePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "ewr", "archive");
            }
        }

        _archivePath = archivePath;
        _logger = logger;
        _timestamps = new Lazy<Task<List<DateTime>>>(LoadTimestampsAsync);

        _logger.LogInformation("Initializing ArchiveRadarDataProvider with path: {ArchivePath}", _archivePath);
        _logger.LogInformation("Directory exists: {Exists}, Current directory: {CurrentDir}",
            Directory.Exists(_archivePath), Directory.GetCurrentDirectory());

        if (!Directory.Exists(_archivePath)) {
            _logger.LogError("Archive directory does not exist: {ArchivePath}", _archivePath);
        }
    }

    public IEnumerable<Channel> GetAvailableChannels() => Enum.GetValues<Channel>();

    public async Task<IEnumerable<DateTime>> GetTimestampsAsync() {
        return await _timestamps.Value;
    }

    public async Task<(DateTime Start, DateTime End)?> GetTimeRangeAsync() {
        var timestamps = await GetTimestampsAsync();
        var timestampList = timestamps.ToList();
        if (timestampList.Count == 0) return null;
        return (timestampList.Min(), timestampList.Max());
    }

    public async Task<IReadOnlyList<float>?> GetElevationAnglesAsync(DateTime timestamp) {
        var files = await GetFilesForTimestampAsync(timestamp);
        if (files == null || files.Count == 0) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }

        _logger.LogInformation("Getting elevations for timestamp {Timestamp}, found {Count} files", timestamp, files.Count);

        var elevations = new List<float>();
        foreach (var filePath in files) {
            try {
                _logger.LogDebug("Reading elevation from {FilePath}", filePath);
                using var scan = new EWRPolarScan(filePath);
                _logger.LogDebug("File {FilePath}: elevation={Elevation}, nRays={NRays}, nBins={NBins}",
                    filePath, scan.ElevationAngle, scan.NRays, scan.NBins);

                // Include all scans, even if they have zero dimensions (might be valid but empty)
                elevations.Add(scan.ElevationAngle);
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

    public async Task<ScanMetadata?> GetScanMetadataAsync(DateTime timestamp, float elevationAngle) {
        var files = await GetFilesForTimestampAsync(timestamp);
        if (files == null || files.Count == 0) {
            _logger.LogWarning("No files found for timestamp {Timestamp}", timestamp);
            return null;
        }

        _logger.LogInformation("Looking for scan: timestamp={Timestamp}, elevation={Elevation}, found {Count} files",
            timestamp, elevationAngle, files.Count);

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

    public async Task<float?> GetValueAsync(Channel channel, DateTime timestamp, float elevationAngle, int ray, int bin) {
        var files = await GetFilesForTimestampAsync(timestamp);
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

    public async Task<float?> GetValueByGeoAsync(Channel channel, DateTime timestamp, float elevationAngle, float latitude, float longitude) {
        var files = await GetFilesForTimestampAsync(timestamp);
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

    public async Task<float[,]?> GetDataAsync(Channel channel, DateTime timestamp, float elevationAngle) {
        var files = await GetFilesForTimestampAsync(timestamp);
        if (files == null || files.Count == 0) return null;

        foreach (var filePath in files) {
            try {
                using var scan = new EWRPolarScan(filePath);
                var elevationDiff = Math.Abs(scan.ElevationAngle - elevationAngle);
                if (elevationDiff < 0.5f) { // Increased tolerance
                    return scan[channel];
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to read data from {FilePath}", filePath);
            }
        }

        return null;
    }

    public async Task<byte[]?> GetImageAsync(Channel channel, DateTime timestamp, float elevationAngle, int width = 1500, int height = 1500) {
        var files = await GetFilesForTimestampAsync(timestamp);
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
                        radarLat: scan.Latitude, radarLon: scan.Longitude, gridSpec: gridSpec);

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
                        radarLat: scan.Latitude, radarLon: scan.Longitude, gridSpec: gridSpec);

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

    private async Task<List<DateTime>> LoadTimestampsAsync() {
        return await Task.Run(() => {
            if (!Directory.Exists(_archivePath)) {
                _logger.LogError("Archive directory not found: {ArchivePath}", _archivePath);
                _logger.LogError("Current working directory: {CurrentDir}", Directory.GetCurrentDirectory());
                _logger.LogError("Trying absolute path: {AbsolutePath}", Path.GetFullPath(_archivePath));
                return new List<DateTime>();
            }

            var h5Files = Directory.GetFiles(_archivePath, "*.h5", SearchOption.TopDirectoryOnly);
            _logger.LogInformation("Found {Count} H5 files in {ArchivePath}", h5Files.Length, _archivePath);

            if (h5Files.Length == 0) {
                _logger.LogWarning("No H5 files found in directory: {ArchivePath}", _archivePath);
                return new List<DateTime>();
            }

            var timestamps = new HashSet<DateTime>();
            int successCount = 0;
            int failCount = 0;

            foreach (var filePath in h5Files) {
                var timestamp = ExtractTimestampFromFile(filePath);
                if (timestamp.HasValue) {
                    timestamps.Add(timestamp.Value);
                    if (!_filesByTimestamp.ContainsKey(timestamp.Value)) {
                        _filesByTimestamp[timestamp.Value] = [];
                    }
                    _filesByTimestamp[timestamp.Value].Add(filePath);
                    successCount++;
                } else {
                    failCount++;
                    _logger.LogDebug("Failed to extract timestamp from file: {FilePath}", filePath);
                }
            }

            _logger.LogInformation("Loaded {Count} unique timestamps from {ArchivePath} (success: {Success}, failed: {Failed})",
                timestamps.Count, _archivePath, successCount, failCount);
            return timestamps.OrderBy(t => t).ToList();
        });
    }

    private async Task<List<string>?> GetFilesForTimestampAsync(DateTime timestamp) {
        // Ensure timestamps are loaded
        await _timestamps.Value;

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
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        // Pattern: EWR followed by 12 digits (YYMMDDHHMMSS)
        var match = Regex.Match(fileName, @"EWR(\d{12})");
        if (match.Success) {
            var timestampStr = match.Groups[1].Value;
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
}

