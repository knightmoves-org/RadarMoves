using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using PureHDF;
using RadarMoves.Server.Dataset;
using System.Reflection.Metadata;

namespace RadarMoves.Test;


public static class RadarImage {
    // Simple Jet-style colormap (blue → green → yellow → red)
    private static Rgba32 JetColor(float t) {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 3f), 0f, 1f) * 255);
        byte g = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 2f), 0f, 1f) * 255);
        byte b = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 1f), 0f, 1f) * 255);
        return new Rgba32(r, g, b);
    }

    public static void SaveRadarImage(float[,] radarData, string filePath) {
        int height = radarData.GetLength(0);
        int width = radarData.GetLength(1);

        // Find min/max for normalization
        float min = float.MaxValue;
        float max = float.MinValue;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float v = radarData[y, x];
                if (float.IsNaN(v)) continue;  // ignore NaNs
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        float range = max - min;
        if (range == 0) range = 1f;

        using var image = new Image<Rgba32>(width, height);


        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float val = radarData[y, x];
                if (float.IsNaN(val)) {
                    image[x, y] = new Rgba32(0, 0, 0, 0);
                } else {
                    float t = (val - min) / range;
                    image[x, y] = JetColor(t);
                }
            }
        }




        image.Save(filePath);
    }
}

public class DatasetTests {
    public static readonly string DATA_ROOT = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
    [Fact]
    public void EarthRadiusConstant_ShouldBeCorrect() {
        // Arrange & Act
        var earthRadius = EWRPolarScan.Re;

        // Assert
        Assert.Equal(6_371_000.0, earthRadius);
    }

    [Fact]

    public void Dataset_Constructor_WithValidHdf5File_ShouldInitialize() {

        // Note: This test requires a valid HDF5 file with the following structure:
        // - Group "where" with attributes: "lat", "lon", "height"
        // - Group "what" with attributes: "date", "time"
        // 
        // To make this test pass, you'll need to:
        // 1. Create a test HDF5 file with the required structure
        // 2. Update the path below to point to your test file
        // 3. Uncomment and adjust the test code below
        // var root = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
        // /home/leaver/RadarMoves/data/EWR250524024617.h5
        var filename = Path.Combine(DATA_ROOT, "data", "ewr", "EWR250524024617.h5");

        var ds = new EWRPolarScan(filename);

        // var shape = ds.Shape;
        var groundRange = ds.GroundRange();

        Assert.Equal(ds.NBins, groundRange.Length);
        // Get radar location
        double radarLat = ds.Latitude;
        double radarLon = ds.Longitude;
        var (latitude, longitude, _, (latMin, latMax, lonMin, lonMax)) = ds.GetGeodeticCoordinates();




        // Radar moment to remap
        Console.WriteLine(Channel.Reflectivity);
        Console.WriteLine((int)Channel.Reflectivity);
        float[,] radarMoment = ds[Channel.Reflectivity];
        float vMin = float.MaxValue;
        float vMax = float.MinValue;
        for (int i = 0; i < radarMoment.GetLength(0); i++) {
            for (int j = 0; j < radarMoment.GetLength(1); j++) {
                if (radarMoment[i, j] < vMin) vMin = radarMoment[i, j];
                if (radarMoment[i, j] > vMax) vMax = radarMoment[i, j];
            }
        }
        Console.WriteLine($"vMin: {vMin}, vMax: {vMax}");
        RadarImage.SaveRadarImage(radarMoment, Path.Combine(DATA_ROOT, "radarMoment.png"));



        int height = 512;
        int width = 512;


        // Call the function
        DateTime endBenchmark;
        DateTime startBenchmark;
        EWRPolarScan.GridSpec gridSpec = new(lonMin, lonMax, latMin, latMax, width, height);
        // assert gridSpec is correct




        startBenchmark = DateTime.Now;

        var (projected, latitudes, longitudes) = ds.InterpolateIDW(radarMoment, gridSpec, isValid: (v) => !float.IsNaN(v) && v > 0.0f);
        endBenchmark = DateTime.Now;
        Console.WriteLine($"ds.ToRaster in {endBenchmark - startBenchmark}");
        Assert.Equal(projected.GetLength(0), height);
        Assert.Equal(projected.GetLength(1), width);
        var outfile = Path.Combine(DATA_ROOT, "projected.png");
        RadarImage.SaveRadarImage(projected, outfile);
        Assert.True(File.Exists(outfile));
        // --------- test that the latitudes and longitudes are in the correct order


    }

    [Fact]
    public void ToRaster_LatLonOrder_ShouldBeCorrect() {
        // Arrange
        var root = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
        var filename = Path.Combine(root, "data", "ewr", "EWR250524024617.h5");
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

