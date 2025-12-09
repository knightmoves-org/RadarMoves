using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using PureHDF;
using PureHDF.VOL.Native;
using System.IO.MemoryMappedFiles;
using static System.Math;



namespace RadarMoves.Server.Data;
public enum Channel {
    TotalPower,    // data1 # TH
    Reflectivity,  // data2 # DBZH
    RadialVelocity,// data3 # VRADH
    SpectralWidth  // data4 # WRADH
}


public static class IH5GroupExtensions {
    // Example: Get an attribute as a typed value
    public static T GetAttribute<T>(this IH5Object obj, string name, T defaultValue = default!) {
        var attr = obj.Attribute(name);
        if (attr == null) return defaultValue;
        return attr.Read<T>();
    }
    public static U GetAttribute<T, U>(this IH5Object obj, string name, U defaultValue = default(U)!)
        where T : IConvertible where U : IConvertible {
        var attr = obj.Attribute(name);
        if (attr == null) return defaultValue;

        // Read the underlying HDF5 value as type T (e.g., string)
        T valueT = attr.Read<T>();

        // Check if the value is a string and U is an enum, as intended
        if (typeof(T) == typeof(string) && typeof(U).IsEnum) {
            string stringValue = valueT.ToString()!;
            try {
                // Parse the string value into the enum type U
                // Ignores case for flexibility
                return (U)Enum.Parse(typeof(U), stringValue, ignoreCase: true);
            } catch (ArgumentException) {
                // Handle cases where the string doesn't match a valid enum value name
                return defaultValue;
            }
        } else {
            // If T wasn't a string or U wasn't an enum, fall back or handle appropriately.
            // In this specific scenario, we return the default if constraints aren't met exactly.
            return (U)Convert.ChangeType(attr.Read<T>(), typeof(U));
        }
    }
}


// Opera Data Information Model (ODIM) HDF5 dataset
public sealed class EWRPolarScan : IDisposable, IRadarDataset<float> {
    // ------------------------------------------------------------------------------------------------------------- //
    // TODO: Move to a separate class
    // ------------------------------------------------------------------------------------------------------------- //
    public const float NoData = -9999f;
    public const double Re = 6_371_000.0;
    public const double Re43 = Re * (4.0 / 3.0);
    private const double DEG2RAD = PI / 180.0;
    private static T GetDeg2Rad<T>() where T : IFloatingPoint<T> => T.CreateChecked(DEG2RAD);
    public static T Deg2Rad<T>(T degrees) where T : IFloatingPoint<T> => degrees * GetDeg2Rad<T>();
    public static T Rad2Deg<T>(T radians) where T : IFloatingPoint<T> => radians / GetDeg2Rad<T>();
    public static Vector<T> Rad2Deg<T>(Vector<T> radians) where T : IFloatingPoint<T> => radians / GetDeg2Rad<T>();
    public static Vector<T> Deg2Rad<T>(Vector<T> degrees) where T : IFloatingPoint<T> => degrees * GetDeg2Rad<T>();
    // ------------------------------------------------------------------------------------------------------------- //
    public static float InverseDistanceWeighting(float distance) => 1.0f / (MathF.Pow(distance, 1.5f) + 0.01f);
    public static float[] Flatten(float[,] array) {
        int rows = array.GetLength(0);
        int cols = array.GetLength(1);
        float[] flat = new float[rows * cols];
        // Copies raw bytes directly — extremely fast
        Buffer.BlockCopy(array, 0, flat, 0, flat.Length * sizeof(float));

        return flat;
    }
    public readonly struct GridSpec(float lonMin, float lonMax, float latMin, float latMax, int width, int height) {
        public readonly float LonMin = lonMin;
        public readonly float LonMax = lonMax;
        public readonly float LatMin = latMin;
        public readonly float LatMax = latMax;
        public readonly int Width = width;
        public readonly int Height = height;
        public float LonRes => (LonMax - LonMin) / Width;
        public float LatRes => (LatMax - LatMin) / Height;
    }


    // ------------------------------------------------------------------------------------------------------------- //
    private readonly NativeFile _file;
    public readonly DateTime Datetime;
    public readonly float Height;
    public readonly float Latitude;
    public readonly float Longitude;
    public readonly int NRays;
    public readonly int NBins;
    public readonly float ElevationAngle;
    public readonly float RScale;
    public readonly float RStart;
    public readonly float A1GateDeg;
    public readonly float[] Azimuths;
    private readonly float[][,] _raw;
    private (double[,] latitude, double[,] longitude, double[,] height, (float latMin, float latMax, float lonMin, float lonMax))? _cachedGeodetic;
    public IEnumerable<Channel> Keys => Enum.GetValues<Channel>();
    public float[][,] Raw => _raw;
    public IEnumerable<float[,]> Values => _raw;
    public float[,] GetFilteredData(Channel c) {
        var filter = new SpeckleRemovalFilter(threshold: 1.0f, minArea: 32);
        var grid = _raw[(int)c].AsSpan();
        filter.Apply(grid);
        return grid.AsArray();
    }
    public float[,] this[Channel c] => GetFilteredData(c);
    public float[,] this[int idx] => this[(Channel)idx];
    public void Dispose() {
        _file?.Dispose();
        GC.SuppressFinalize(this);
    }
    public EWRPolarScan(string filePath, H5ReadOptions? options = null) : this(H5File.OpenRead(filePath, options)) { }
    public EWRPolarScan(Stream stream, bool leaveOpen = false, H5ReadOptions? options = null) : this(H5File.Open(stream, leaveOpen, options)) { }
    public EWRPolarScan(MemoryMappedViewAccessor accessor, H5ReadOptions? options = default) : this(H5File.Open(accessor, options)) { }

    public EWRPolarScan(NativeFile file) {
        _file = file;
        IH5Group where, what, how, root;

        where = file.Group("where");
        Latitude = where.GetAttribute<double, float>("lat");
        Longitude = where.GetAttribute<double, float>("lon");
        Height = where.GetAttribute<double, float>("height");

        what = file.Group("what");
        string date = what.GetAttribute<string>("date");
        string time = what.GetAttribute<string>("time");
        Datetime = DateTime.ParseExact($"{date}{time}", "yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        root = file.Group("dataset1");
        where = root.Group("where");
        NRays = where.GetAttribute<double, int>("nrays");
        NBins = where.GetAttribute<double, int>("nbins");
        ElevationAngle = where.GetAttribute<double, float>("elangle");
        RScale = where.GetAttribute<double, float>("rscale");
        RStart = where.GetAttribute<double, float>("rstart");

        how = root.Group("how");
        double[] startAz = how.GetAttribute<double[]>("startazA");
        double[] stopAz = how.GetAttribute<double[]>("stopazA");

        string[] keys = ["data1", "data2", "data3", "data4"];

        _raw = new float[keys.Length][,];

        // Try to read dimensions from the actual dataset if attributes are 0
        bool dimensionsFromData = false;
        if (NRays == 0 || NBins == 0) {
            // Try to get dimensions from the first data dataset
            try {
                var firstDataGroup = root.Group(keys[0]);
                var firstDataset = firstDataGroup.Dataset("data");
                var dims = firstDataset.Space.Dimensions;
                if (dims.Length >= 2) {
                    NRays = (int)dims[0];
                    NBins = (int)dims[1];
                    dimensionsFromData = true;
                }
            } catch {
                // If we can't read from dataset, keep the attribute values (which are 0)
            }
        }

        // calculate azimuth from start and stop azimuth angles
        if (NRays > 0 && startAz != null && stopAz != null && startAz.Length == NRays && stopAz.Length == NRays) {
            Azimuths = new float[NRays]; // averaged azimuth in degrees
            for (int i = 0; i < NRays; i++)
                Azimuths[i] = (float)((startAz[i] + stopAz[i]) / 2.0);
        } else {
            Azimuths = Array.Empty<float>();
        }

        for (int k = 0; k < keys.Length; k++) {
            var key = keys[k];
            var g = root.Group(key);
            what = g.Group("what");
            float gain = what.GetAttribute<double, float>("gain");
            float offset = what.GetAttribute<double, float>("offset");

            try {
                var src = g.Dataset("data").Read<float[,]>();

                // If we got dimensions from data, verify they match
                if (dimensionsFromData && (src.GetLength(0) != NRays || src.GetLength(1) != NBins)) {
                    NRays = src.GetLength(0);
                    NBins = src.GetLength(1);
                }

                if (gain != 1.0f || offset != 0.0f) {
                    // Allocate the array before writing to it
                    _raw[k] = new float[NRays, NBins];
                    for (int i = 0; i < NRays; i++)
                        for (int j = 0; j < NBins; j++)
                            _raw[k][i, j] = src[i, j] * gain + offset;
                } else {
                    _raw[k] = src;
                }
            } catch (Exception ex) {
                // If we can't read the dataset, create an empty array
                _raw[k] = new float[NRays, NBins];
                System.Diagnostics.Debug.WriteLine($"Failed to read {key}: {ex.Message}");
            }
        }
        _cachedGeodetic = null;
    }



    // ------------------------------------------------------------------------------------------------------------- //
    // SIMD-optimized ground range
    // ------------------------------------------------------------------------------------------------------------- //
    public double[] GroundRange() {
        var cosAngle = Cos(Deg2Rad(ElevationAngle));
        var offset = RScale / 2.0;
        var result = new double[NBins];

        int vectorWidth = Vector<double>.Count; // e.g., 2,4,8 depending on hardware
        int i = 0;

        // Vectorized loop
        if (vectorWidth > 1) {
            var rv = new Vector<double>(RScale);
            var rstartVec = new Vector<double>(RStart + offset);
            var baseIdx = new double[vectorWidth];
            for (int k = 0; k < vectorWidth; k++) baseIdx[k] = k;
            var idxVec = new Vector<double>(baseIdx);

            while (i <= NBins - vectorWidth) {
                // slantRange = rstart + (i + lane) * rscale + offset
                var slantRangeVec = rstartVec + (idxVec + new Vector<double>(i)) * rv;
                var outVec = slantRangeVec * new Vector<double>(cosAngle);
                outVec.CopyTo(result, i);
                i += vectorWidth;
            }
        }

        // Remainder
        for (; i < NBins; i++) {
            var slantRange = RStart + (i * RScale) + offset;
            result[i] = slantRange * cosAngle;
        }

        return result;
    }

    /// <summary>
    /// Thread-local structure to hold min/max values for parallel processing.
    /// </summary>
    private struct ThreadLocalMinMax {
        public float LatMin;
        public float LatMax;
        public float LonMin;
        public float LonMax;

        public ThreadLocalMinMax() {
            LatMin = float.MaxValue;
            LatMax = float.MinValue;
            LonMin = float.MaxValue;
            LonMax = float.MinValue;
        }
    }

    /// <summary>
    /// Compute geodetic coordinates (lat/lon/height) for each bin.
    /// Parallelized across rays. The result is cached on first call.
    /// </summary>
    public (double[,] latitude, double[,] longitude, double[,] height, (float latMin, float latMax, float lonMin, float lonMax))
      GetGeodeticCoordinates() {
        if (_cachedGeodetic.HasValue) return _cachedGeodetic.Value;

        double[] groundRange = GroundRange();
        var latitude = new double[NRays, NBins];
        var longitude = new double[NRays, NBins];
        var height = new double[NRays, NBins];



        double lat0 = Deg2Rad(Latitude);
        double lon0 = Deg2Rad(Longitude);

        // Precompute constants for speed
        double invRe = 1.0 / Re;
        double cosLat0 = Cos(lat0);
        double sinLat0 = Sin(lat0);
        double el = Deg2Rad(ElevationAngle);
        double sinEl = Sin(el);
        double cosEl = Cos(el);

        // Thread-local min/max values to avoid race conditions
        float latMin = float.MaxValue;
        float latMax = float.MinValue;
        float lonMin = float.MaxValue;
        float lonMax = float.MinValue;

        // Use object to hold thread-local state for synchronization
        object lockObj = new();

        Parallel.For(0, NRays,
            // localInit - initialize thread-local min/max values
            () => new ThreadLocalMinMax(),
            // body
            (i, loopState, local) => {
                double az = Deg2Rad(Azimuths[i]);
                double cosAz = Cos(az);
                double sinAz = Sin(az);

                for (int j = 0; j < NBins; j++) {
                    double r = groundRange[j];

                    // Beam height above radar (FIXED: use el, not Deg2Rad(elangle) again)
                    double h = Sqrt(r * r + Re * Re + 2.0 * r * Re * sinEl) - Re;

                    // Surface arc distance
                    double s = Re * Asin(r * cosEl / (Re + h));

                    // Angular distance
                    double sigma = s * invRe;

                    // Geodetic latitude
                    double lat = Asin(sinLat0 * Cos(sigma) + cosLat0 * Sin(sigma) * cosAz);

                    // Geodetic longitude
                    double lon = lon0 + Atan2(sinAz * Sin(sigma) * cosLat0, Cos(sigma) - sinLat0 * Sin(lat));

                    // Normalize lon to -pi..pi
                    lon = IEEERemainder(lon + PI, 2.0 * PI) - PI;

                    double latDeg = Rad2Deg(lat);
                    double lonDeg = Rad2Deg(lon);

                    latitude[i, j] = latDeg;
                    longitude[i, j] = lonDeg;
                    height[i, j] = h;

                    // Update thread-local min/max values
                    float latF = (float)latDeg;
                    float lonF = (float)lonDeg;
                    if (latF < local.LatMin) local.LatMin = latF;
                    if (latF > local.LatMax) local.LatMax = latF;
                    if (lonF < local.LonMin) local.LonMin = lonF;
                    if (lonF > local.LonMax) local.LonMax = lonF;
                }

                return local;
            },
            // localFinally - reduce thread-local values into global min/max
            (local) => {
                lock (lockObj) {
                    if (local.LatMin < latMin) latMin = local.LatMin;
                    if (local.LatMax > latMax) latMax = local.LatMax;
                    if (local.LonMin < lonMin) lonMin = local.LonMin;
                    if (local.LonMax > lonMax) lonMax = local.LonMax;
                }
            }
        );

        _cachedGeodetic = (latitude, longitude, height, (latMin, latMax, lonMin, lonMax));
        return _cachedGeodetic.Value;
    }


    /// <summary>
    /// Special nodata value used to mark pixels outside radar coverage bounds (rendered as black).
    /// NaN is used for other nodata cases (rendered as transparent).
    /// </summary>


    public (float[,] raster, float[,] latitudes, float[,] longitudes) InterpolateIDW(
        float[,] radarData,
        GridSpec gs = default,
        Predicate<float>? isValid = default,
        float maxDistance = 2.0f,
        float minWeight = 0.01f,
        int minValidCount = 2
    ) {
        isValid ??= (v) => !float.IsNaN(v);
        // Grid defaults (example: bounding box centered around radar ± 150 km)
        if (gs.Equals(default(GridSpec))) {
            float span = 300_000.0f / 111_000.0f; // ~300km -> degrees approx (coarse)
            gs = new GridSpec(
                Longitude - span, Longitude + span,
                Latitude - span, Latitude + span, 1500, 1500);
        }



        int width = gs.Width;
        int height = gs.Height;
        float lonMin = gs.LonMin;
        float lonMax = gs.LonMax;
        float latMin = gs.LatMin;
        float latMax = gs.LatMax;
        float lonRes = gs.LonRes;
        float latRes = gs.LatRes;

        // Earth constants
        float cosEl = MathF.Cos(Deg2Rad(ElevationAngle));
        float cosLat = MathF.Cos(Deg2Rad(Latitude));
        float invR = Rad2Deg(1.0f / (float)Re43);
        float lambda = Rad2Deg(1.0f / ((float)Re43 * cosLat));

        // Thread-local buffers for accumulation
        ConcurrentBag<(float[,] raster, float[,] latitudes, float[,] longitudes, float[,] weights, int[,] counts)> buffers = [];

        // Parallel over rays (outer loop)
        Parallel.For(0, NRays,
            // localInit - create thread-local accumulation arrays
            () => {
                var raster = new float[height, width];
                var weights = new float[height, width]; // Use float for bilinear interpolation weights
                var counts = new int[height, width]; // Count of valid values contributing to each pixel
                var latitudes = new float[height, width];
                latitudes.Fill(float.NaN);
                var longitudes = new float[height, width];
                longitudes.Fill(float.NaN);

                buffers.Add((raster, latitudes, longitudes, weights, counts));
                return (raster, latitudes, longitudes, weights, counts);
            },
            // body
            (i, loopState, local) => {
                // Compute azimuth for this ray (degrees -> radians)
                float azRad = Deg2Rad(Azimuths[i]);
                float sinAz = MathF.Sin(azRad);
                float cosAz = MathF.Cos(azRad);

                // Process all bins for this ray (scalar loop, no SIMD)
                for (int j = 0; j < NBins; j++) {
                    // Read from 2D array directly
                    float value = radarData[i, j];

                    // Always calculate range and coordinates, even for nodata Calculate range
                    float r = RStart + j * RScale;

                    // Ground range approximate (cos(el)*r)
                    float g = cosEl * r;

                    // Convert to Cartesian (east, north)
                    float y = g * cosAz;
                    float x = g * sinAz;

                    // Convert to lat/lon degrees
                    float lat = Latitude + y * invR;
                    float lon = Longitude + x * lambda;

                    // Map lat/lon into grid indices using inverse distance weighting
                    // Use a 3x3 kernel to fill gaps between radar rays at higher resolutions
                    float pyf = (latMax - lat) / latRes;
                    float pxf = (lon - lonMin) / lonRes;

                    // Get the center pixel (use floor to be consistent with grid indexing)
                    int pxi = (int)MathF.Floor(pxf);
                    int pyi = (int)MathF.Floor(pyf);
                    // Accumulate to a 3x3 neighborhood around the target pixel
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            int px = pxi + dx;
                            int py = pyi + dy;

                            // Calculate distance from actual position to pixel center
                            float dxf = pxf - px;
                            float dyf = pyf - py;
                            float distance = MathF.Sqrt(dxf * dxf + dyf * dyf);

                            // Skip if too far
                            if (distance > maxDistance) continue;
                            float weight = InverseDistanceWeighting(distance);
                            // Distribute value to nearby pixels using inverse distance weighting
                            if (px >= 0 && px < width && py >= 0 && py < height) {
                                // Store lat/lon for the center pixel
                                if (px == pxi && py == pyi) {
                                    local.latitudes[py, px] = lat;
                                    local.longitudes[py, px] = lon;
                                }

                                // Accumulate weighted value if valid
                                if (isValid(value) && weight > 0.001f) { // Only accumulate significant weights
                                    local.raster[py, px] += value * weight;
                                    local.weights[py, px] += weight;
                                    local.counts[py, px] += 1; // Count each valid value contribution
                                }
                            }
                        }
                    }
                }

                return local;
            },
            (local) => { /* no-op */ }
        );


        (float[,] raster, float[,] latitudes, float[,] longitudes, float[,] weights, int[,] counts) final = (
            new float[height, width], new float[height, width], new float[height, width], new float[height, width], new int[height, width]
        );


        // Initialize final lat/lon to NaN
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                final.latitudes[y, x] = float.NaN;
                final.longitudes[y, x] = float.NaN;
            }
        }

        // Reduce: sum thread-local buffers into final arrays
        foreach (var (raster, latitudes, longitudes, weights, counts) in buffers) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    float v = raster[y, x];
                    float w = weights[y, x];
                    int c = counts[y, x];
                    float lat = latitudes[y, x];
                    float lon = longitudes[y, x];

                    // Always accumulate data, weights, and counts if there's data
                    if (w > 0) {
                        final.raster[y, x] += v;
                        final.weights[y, x] += w;
                        final.counts[y, x] += c;
                    }

                    // Always preserve lat/lon if they were calculated (not NaN)
                    // If multiple threads wrote to same pixel, last one wins
                    if (!float.IsNaN(lat) && !float.IsNaN(lon)) {
                        final.latitudes[y, x] = lat;
                        final.longitudes[y, x] = lon;
                    }
                }
            }
        }

        // Get actual radar data bounds and calculate maximum ground range
        var (_, _, _, (dataLatMin, dataLatMax, dataLonMin, dataLonMax)) = GetGeodeticCoordinates();
        double[] groundRange = GroundRange();
        float maxGroundRange = (float)groundRange[groundRange.Length - 1]; // Maximum range in meters
        float maxGroundRangeKm = maxGroundRange / 1000f; // Convert to km for distance calculation

        // Average where weights > 0; where weights==0 leave as nodata (NaN)
        // Calculate lat/lon for ALL pixels based on grid coordinates
        // Filter out pixels with too little weight or too few valid values to reduce clutter
        (float[,] raster, float[,] latitudes, float[,] longitudes) output = (new float[height, width], new float[height, width], new float[height, width]);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float w = final.weights[y, x];
                int c = final.counts[y, x];

                // Calculate lat/lon for this pixel from grid coordinates
                // If lat/lon was already calculated from radar data, use that; otherwise calculate from grid position
                float pixelLat, pixelLon;
                if (!float.IsNaN(final.latitudes[y, x]) && !float.IsNaN(final.longitudes[y, x])) {
                    // Use the lat/lon calculated from radar data
                    pixelLat = final.latitudes[y, x];
                    pixelLon = final.longitudes[y, x];
                    output.latitudes[y, x] = pixelLat;
                    output.longitudes[y, x] = pixelLon;
                } else {
                    // Calculate lat/lon from grid pixel center coordinates
                    pixelLon = lonMin + (x + 0.5f) * lonRes;
                    pixelLat = latMax - (y + 0.5f) * latRes; // (assuming y=0 is top/north)
                    output.longitudes[y, x] = pixelLon;
                    output.latitudes[y, x] = pixelLat;
                }

                // Calculate distance from radar center to this pixel
                // Using Haversine formula for accurate distance calculation
                double lat1Rad = Deg2Rad(Latitude);
                double lon1Rad = Deg2Rad(Longitude);
                double lat2Rad = Deg2Rad(pixelLat);
                double lon2Rad = Deg2Rad(pixelLon);

                double dLat = lat2Rad - lat1Rad;
                double dLon = lon2Rad - lon1Rad;
                double a = Sin(dLat / 2) * Sin(dLat / 2) +
                          Cos(lat1Rad) * Cos(lat2Rad) *
                          Sin(dLon / 2) * Sin(dLon / 2);
                double haversineC = 2 * Atan2(Sqrt(a), Sqrt(1 - a));
                double distanceMeters = Re * haversineC;
                float distanceKm = (float)(distanceMeters / 1000.0);

                // Check if pixel is outside radar coverage:
                // 1. Outside rectangular bounds, OR
                // 2. Beyond maximum ground range
                // Pixels inside bounds but with no data should be NaN (transparent), not NoData (black)
                bool outsideBounds = pixelLat < dataLatMin || pixelLat > dataLatMax || pixelLon < dataLonMin || pixelLon > dataLonMax;
                bool beyondRange = distanceKm > maxGroundRangeKm * 1.05f; // 5% margin for edge cases

                if (outsideBounds || beyondRange) {
                    // Pixel is outside radar coverage bounds - mark as nodata value (will render as black)
                    output.raster[y, x] = NoData;
                } else {
                    // Pixel is inside radar coverage bounds
                    // Require both minimum weight AND minimum count of valid values
                    if (w < minWeight || c < minValidCount) {
                        // Not enough weight or not enough valid values - mark as nodata (transparent)
                        output.raster[y, x] = float.NaN;
                    } else {
                        // Average the accumulated weighted values
                        output.raster[y, x] = final.raster[y, x] / w;
                    }
                }
            }
        }

        return output;
    }


}
