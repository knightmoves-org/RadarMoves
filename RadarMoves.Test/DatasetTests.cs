using RadarMoves.Server.Data;


namespace RadarMoves.Test;


public class DatasetTests {
    public static readonly string ROOT = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
    public static readonly string DATA_ROOT = Path.Combine(ROOT, "data");
    public static readonly string EWR_ROOT = Path.Combine(DATA_ROOT, "ewr", "samples");
    public static readonly string DOCUMENTS = Path.Combine(ROOT, "Documents");
    public static readonly string IMAGES = Path.Combine(DOCUMENTS, "Images");



    [Fact]
    public void EarthRadiusConstant_ShouldBeCorrect() {
        // Arrange & Act
        var earthRadius = EWRPolarScan.Re;

        // Assert
        Assert.Equal(6_371_000.0, earthRadius);
    }

    [Fact]

    public void Dataset_Constructor_WithValidHdf5File_ShouldInitialize() {
        ImageWriter writer;
        DateTime endBenchmark;
        DateTime startBenchmark;
        int height = 1024;
        int width = 1024;
        var filename = Path.Combine(EWR_ROOT, "EWR250524024617.h5");
        var ds = new EWRPolarScan(filename);

        var groundRange = ds.GroundRange();

        Assert.Equal(ds.NBins, groundRange.Length);

        double radarLat = ds.Latitude;
        double radarLon = ds.Longitude;
        var (latitude, longitude, _, (latMin, latMax, lonMin, lonMax)) = ds.GetGeodeticCoordinates();




        EWRPolarScan.GridSpec gridSpec = new(lonMin, lonMax, latMin, latMax, width, height);
        foreach (var k in ds.Keys) {
            var raw = ds.Raw[(int)k];
            writer = new(raw);
            writer.SavePNG(Path.Combine(IMAGES, $"Raw{k}.png"), channel: k, includeColorBar: true);
            var filtered = ds[k];
            writer = new(filtered);
            writer.SavePNG(Path.Combine(IMAGES, $"Filtered{k}.png"), channel: k, includeColorBar: true);
        }


        for (int i = 0; i < ds.Keys.Count(); i++) {
            var key = ds.Keys.ElementAt(i);
            float[,] radar = ds[key];

            startBenchmark = DateTime.Now;
            var (projected, latitudes, longitudes) = ds.InterpolateIDW(radar, gridSpec, isValid: (v) => !float.IsNaN(v), maxDistance: 1.2f, minWeight: 1.0f, minValidCount: 2);
            endBenchmark = DateTime.Now;
            Console.WriteLine($"ds.ToRaster in {endBenchmark - startBenchmark}");
            Assert.Equal(projected.GetLength(0), height);
            Assert.Equal(projected.GetLength(1), width);
            // we want to put each of the projected images in a 4x4 grid

            // --------- test that the latitudes and longitudes are in the correct order

            var outfile = Path.Combine(IMAGES, $"Projected{key}.png");
            writer = new(projected);
            writer.SavePNG(outfile, channel: key, includeColorBar: true,
                radarLat: (float)radarLat, radarLon: (float)radarLon, gridSpec: gridSpec);
        }
    }

    [Fact]
    public void ToRaster_LatLonOrder_ShouldBeCorrect() {
        // Arrange

        var filename = Path.Combine(EWR_ROOT, "EWR250524024617.h5");
        var ds = new EWRPolarScan(filename);
        float[,] radarMoment = ds[Channel.Reflectivity];

        int height = 256;
        int width = 256;
        float radarLat = ds.Latitude;
        float radarLon = ds.Longitude;
        float latMin = (float)(radarLat - 2.0);
        float latMax = (float)(radarLat + 2.0);
        float lonMin = (float)(radarLon - 2.0);
        float lonMax = (float)(radarLon + 2.0);

        EWRPolarScan.GridSpec gridSpec = new(lonMin, lonMax, latMin, latMax, width, height);

        // Act
        var (projected, latitudes, longitudes) = ds.InterpolateIDW(radarMoment, gridSpec);

        // Assert - Check dimensions
        Assert.Equal(height, latitudes.GetLength(0));
        Assert.Equal(width, latitudes.GetLength(1));
        Assert.Equal(height, longitudes.GetLength(0));
        Assert.Equal(width, longitudes.GetLength(1));

        // Assert - Check that all values are valid (not NaN)
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                Assert.False(float.IsNaN(latitudes[y, x]), $"Latitude at [{y}, {x}] should not be NaN");
                Assert.False(float.IsNaN(longitudes[y, x]), $"Longitude at [{y}, {x}] should not be NaN");
            }
        }

        // Assert - Check that latitude decreases as y increases (y=0 is top/north, higher latitude)
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height - 1; y++) {
                float latCurrent = latitudes[y, x];
                float latNext = latitudes[y + 1, x];
                Assert.True(latCurrent >= latNext,
                    $"Latitude should decrease as y increases. At x={x}, y={y}: {latCurrent} should be >= {latNext}");
            }
        }

        // Assert - Check that longitude increases as x increases (x=0 is left/west, lower longitude)
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width - 1; x++) {
                float lonCurrent = longitudes[y, x];
                float lonNext = longitudes[y, x + 1];
                Assert.True(lonCurrent <= lonNext,
                    $"Longitude should increase as x increases. At y={y}, x={x}: {lonCurrent} should be <= {lonNext}");
            }
        }

        // Assert - Check that values are within expected bounds (with small tolerance for floating point)
        const float tolerance = 0.01f; // Allow small tolerance for floating point precision
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float lat = latitudes[y, x];
                float lon = longitudes[y, x];

                Assert.True(lat >= latMin - tolerance && lat <= latMax + tolerance,
                    $"Latitude at [{y}, {x}] = {lat} should be between {latMin} and {latMax}");
                Assert.True(lon >= lonMin - tolerance && lon <= lonMax + tolerance,
                    $"Longitude at [{y}, {x}] = {lon} should be between {lonMin} and {lonMax}");
            }
        }

        // Assert - Check corner values are approximately correct
        float topLeftLat = latitudes[0, 0];
        float topLeftLon = longitudes[0, 0];
        float topRightLat = latitudes[0, width - 1];
        float topRightLon = longitudes[0, width - 1];
        float bottomLeftLat = latitudes[height - 1, 0];
        float bottomLeftLon = longitudes[height - 1, 0];
        float bottomRightLat = latitudes[height - 1, width - 1];
        float bottomRightLon = longitudes[height - 1, width - 1];

        // Top row should have higher latitude (north)
        Assert.True(topLeftLat > bottomLeftLat, "Top-left should have higher latitude than bottom-left");
        Assert.True(topRightLat > bottomRightLat, "Top-right should have higher latitude than bottom-right");

        // Right column should have higher longitude (east)
        Assert.True(topRightLon > topLeftLon, "Top-right should have higher longitude than top-left");
        Assert.True(bottomRightLon > bottomLeftLon, "Bottom-right should have higher longitude than bottom-left");
    }
}

