using System.Collections.ObjectModel;

namespace RadarMoves.Server.Data;

/// <summary>
/// Represents a Polar Volume containing multiple elevation angles (scans) at a single timestamp.
/// Per ODIM 2.4 documentation, a PolarVolume is a sequence of elevation angles.
/// Can be constructed from multiple files (one per elevation) or a single file.
/// </summary>
public sealed class EWRPolarVolume : IDisposable {
    private readonly List<EWRPolarScan> _scans;
    private readonly Dictionary<float, EWRPolarScan> _elevationIndex;
    private bool _disposed = false;

    /// <summary>
    /// All scans in this volume, ordered by elevation angle
    /// </summary>
    public ReadOnlyCollection<EWRPolarScan> Scans { get; }

    /// <summary>
    /// Timestamp of this volume (from the first scan)
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Radar location (assumed consistent across all scans)
    /// </summary>
    public (float Latitude, float Longitude, float Height) Location { get; }

    /// <summary>
    /// All elevation angles in this volume, sorted
    /// </summary>
    public IReadOnlyList<float> ElevationAngles { get; }

    /// <summary>
    /// Number of elevation angles in this volume
    /// </summary>
    public int Count => _scans.Count;

    /// <summary>
    /// Get a scan by elevation angle
    /// </summary>
    public EWRPolarScan? GetScanByElevation(float elevationAngle) {
        return _elevationIndex.TryGetValue(elevationAngle, out var scan) ? scan : null;
    }

    /// <summary>
    /// Get a scan by index (ordered by elevation angle)
    /// </summary>
    public EWRPolarScan this[int index] => _scans[index];

    /// <summary>
    /// Get a scan by elevation angle
    /// </summary>
    public EWRPolarScan? this[float elevationAngle] => GetScanByElevation(elevationAngle);

    /// <summary>
    /// Create a PolarVolume from multiple file paths (one per elevation angle)
    /// </summary>
    public EWRPolarVolume(IEnumerable<string> filePaths) {
        _scans = [];
        _elevationIndex = [];

        foreach (var filePath in filePaths) {
            var scan = new EWRPolarScan(filePath);
            _scans.Add(scan);
            _elevationIndex[scan.ElevationAngle] = scan;
        }

        // Sort by elevation angle
        _scans.Sort((a, b) => a.ElevationAngle.CompareTo(b.ElevationAngle));

        Scans = _scans.AsReadOnly();
        ElevationAngles = _scans.Select(s => s.ElevationAngle).ToList().AsReadOnly();

        // Use first scan's timestamp and location (assumed consistent)
        if (_scans.Count > 0) {
            Timestamp = _scans[0].Datetime;
            Location = (_scans[0].Latitude, _scans[0].Longitude, _scans[0].Height);
        } else {
            throw new ArgumentException("At least one scan is required to create a PolarVolume", nameof(filePaths));
        }
    }

    /// <summary>
    /// Create a PolarVolume from a single file (single elevation angle)
    /// </summary>
    public EWRPolarVolume(string filePath) : this([filePath]) { }

    /// <summary>
    /// Create a PolarVolume from multiple EWRPolarScan objects
    /// </summary>
    public EWRPolarVolume(IEnumerable<EWRPolarScan> scans) {
        _scans = [.. scans];
        _elevationIndex = new Dictionary<float, EWRPolarScan>();

        foreach (var scan in _scans) {
            _elevationIndex[scan.ElevationAngle] = scan;
        }

        // Sort by elevation angle
        _scans.Sort((a, b) => a.ElevationAngle.CompareTo(b.ElevationAngle));

        Scans = _scans.AsReadOnly();
        ElevationAngles = _scans.Select(s => s.ElevationAngle).ToList().AsReadOnly();

        if (_scans.Count > 0) {
            Timestamp = _scans[0].Datetime;
            Location = (_scans[0].Latitude, _scans[0].Longitude, _scans[0].Height);
        } else {
            throw new ArgumentException("At least one scan is required to create a PolarVolume", nameof(scans));
        }
    }

    public void Dispose() {
        if (!_disposed) {
            foreach (var scan in _scans) {
                scan.Dispose();
            }
            _scans.Clear();
            _elevationIndex.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

