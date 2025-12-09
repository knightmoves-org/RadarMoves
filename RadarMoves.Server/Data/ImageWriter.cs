using System.Xml.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace RadarMoves.Server.Data;

/// <summary>
/// Represents the threshold range (min/max) for a radar channel.
/// </summary>
public record ChannelThreshold(float ValueMin, float ValueMax);

/// <summary>
/// Provides standard threshold values for radar channels used for consistent color bar scaling.
/// Based on typical weather radar standards (ODIM HDF5 conventions).
/// </summary>
public static class ImageThreshold {
    public static readonly Dictionary<Channel, ChannelThreshold> Thresholds = new() {
        // Reflectivity (DBZH): Typical range for weather radar is -32 to 75 dBZ
        // Common display range: -20 to 70 dBZ
        [Channel.Reflectivity] = new(ValueMin: -32f, ValueMax: 75f),

        // Radial Velocity (VRADH): Typical range depends on PRF, common ranges:
        // Low PRF: -32 to 32 m/s, High PRF: -64 to 64 m/s
        // Using common weather radar range: -64 to 64 m/s
        [Channel.RadialVelocity] = new(ValueMin: -64f, ValueMax: 64f),

        // Spectral Width (WRADH): Typical range 0 to 16 m/s
        // Common display range: 0 to 8 m/s for most weather applications
        [Channel.SpectralWidth] = new(ValueMin: 0f, ValueMax: 16f),

        // Total Power (TH): Less standardized, typically 0 to 100 dB or similar
        // Using a common range based on typical radar power measurements
        [Channel.TotalPower] = new(ValueMin: 0f, ValueMax: 100f)
    };

    /// <summary>
    /// Gets the threshold for a specific channel, or null if not found.
    /// </summary>
    public static ChannelThreshold Get(Channel channel) {
        return Thresholds.TryGetValue(channel, out var threshold) ? threshold : throw new ArgumentException($"Channel {channel} not found in ImageThreshold.Thresholds");
    }

    /// <summary>
    /// Gets the threshold for a specific channel, or returns default values if not found.
    /// </summary>
    public static ChannelThreshold GetThresholdOrDefault(Channel channel, float defaultMin = 0f, float defaultMax = 100f) {
        return Thresholds.TryGetValue(channel, out var threshold) ? threshold : new ChannelThreshold(defaultMin, defaultMax);
    }

}
public class ImageWriter(float[,] data) {
    public readonly float[,] Data = data;
    public int Height => Data.GetLength(0);
    public int Width => Data.GetLength(1);


    // Simple Jet-style colormap (blue → green → yellow → red)
    private static Rgba32 JetColor(float t) {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 3f), 0f, 1f) * 255);
        byte g = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 2f), 0f, 1f) * 255);
        byte b = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 1f), 0f, 1f) * 255);
        return new Rgba32(r, g, b);
    }

    // Convert Rgba32 to hex color string for SVG
    private static string ColorToHex(Rgba32 color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    // Draw range rings on PNG image
    private static void DrawRangeRingsPNG(Image<Rgba32> image, float radarLat, float radarLon, EWRPolarScan.GridSpec gridSpec, int imageDataWidth, int imageDataHeight) {
        // Range rings in nautical miles
        float[] rangeRingsNm = [5f, 10f, 25f, 50f];

        // Convert nautical miles to meters (1 nautical mile = 1852 meters)
        const float nmToMeters = 1852f;
        // Approximate degrees per meter: 1 degree latitude ≈ 111,000 meters
        // For longitude, it varies with latitude: 1 degree longitude ≈ 111,000 * cos(latitude) meters
        const float metersPerDegreeLat = 111000f;

        // Calculate radar center position in pixels (only on main image area, not color bar)
        float centerX = (radarLon - gridSpec.LonMin) / gridSpec.LonRes;
        float centerY = (gridSpec.LatMax - radarLat) / gridSpec.LatRes; // y=0 is top (north)

        // Validate center is within bounds
        if (float.IsNaN(centerX) || float.IsNaN(centerY) || float.IsInfinity(centerX) || float.IsInfinity(centerY)) {
            return; // Invalid center, skip drawing
        }

        // Semi-transparent red color for range rings (alpha = 180/255 ≈ 70% opacity)
        // Using fully opaque for debugging - will make semi-transparent after verification
        var red = new Rgba32(255, 0, 0, 255);

        // First, draw all the circles directly on the image (outside Mutate block)
        foreach (float rangeNm in rangeRingsNm) {
            float rangeMeters = rangeNm * nmToMeters;
            // Convert to degrees (use latitude for approximation, longitude will be similar at mid-latitudes)
            float rangeDegreesLat = rangeMeters / metersPerDegreeLat;
            float rangeDegreesLon = rangeMeters / (metersPerDegreeLat * MathF.Cos(radarLat * MathF.PI / 180f));

            // Calculate radius in pixels (use average of lat/lon conversions)
            float radiusPixelsLat = rangeDegreesLat / gridSpec.LatRes;
            float radiusPixelsLon = rangeDegreesLon / gridSpec.LonRes;
            float radiusPixels = (radiusPixelsLat + radiusPixelsLon) / 2f;

            // Validate radius
            if (float.IsNaN(radiusPixels) || float.IsInfinity(radiusPixels) || radiusPixels <= 0) {
                continue; // Skip invalid radius
            }

            // Draw circle using Bresenham-like algorithm
            int radius = (int)Math.Round(radiusPixels);
            int cx = (int)Math.Round(centerX);
            int cy = (int)Math.Round(centerY);

            // Only draw if center is within reasonable bounds (allow some margin for large rings)
            if (cx < -imageDataWidth || cx > imageDataWidth * 2 || cy < -imageDataHeight || cy > imageDataHeight * 2) {
                continue; // Skip this ring if center is way out of bounds
            }

            // Draw thin circle outline (single pixel width, but draw 2 pixels for visibility)
            for (int angle = 0; angle < 360; angle++) {
                double angleRad = angle * Math.PI / 180.0;
                int x = cx + (int)Math.Round(radius * Math.Cos(angleRad));
                int y = cy + (int)Math.Round(radius * Math.Sin(angleRad));

                // Only draw on main image area (not color bar)
                // Draw a 2x2 pixel block for better visibility
                for (int dx = -1; dx <= 1; dx++) {
                    for (int dy = -1; dy <= 1; dy++) {
                        int px = x + dx;
                        int py = y + dy;
                        if (px >= 0 && px < imageDataWidth && py >= 0 && py < imageDataHeight) {
                            // For debugging: use fully opaque red
                            // Later: blend with existing pixel for transparency
                            image[px, py] = red;
                        }
                    }
                }
            }
        }

        // Then, add text labels using Mutate (for font rendering)
        try {
            var fontCollection = new FontCollection();
            fontCollection.AddSystemFonts();
            FontFamily fontFamily;
            if (fontCollection.Families.Any()) {
                fontFamily = fontCollection.Families.First();
            } else {
                fontFamily = fontCollection.Add("Arial");
            }
            var font = fontFamily.CreateFont(10, FontStyle.Bold);

            image.Mutate(ctx => {
                foreach (float rangeNm in rangeRingsNm) {
                    float rangeMeters = rangeNm * nmToMeters;
                    float rangeDegreesLat = rangeMeters / metersPerDegreeLat;
                    float rangeDegreesLon = rangeMeters / (metersPerDegreeLat * MathF.Cos(radarLat * MathF.PI / 180f));

                    float radiusPixelsLat = rangeDegreesLat / gridSpec.LatRes;
                    float radiusPixelsLon = rangeDegreesLon / gridSpec.LonRes;
                    float radiusPixels = (radiusPixelsLat + radiusPixelsLon) / 2f;

                    int radius = (int)Math.Round(radiusPixels);
                    int cx = (int)Math.Round(centerX);
                    int cy = (int)Math.Round(centerY);

                    // Add label at the top of the ring (north, angle = -90 degrees)
                    double labelAngleRad = -90.0 * Math.PI / 180.0; // Top (north)
                    float labelX = cx + (float)(radius * Math.Cos(labelAngleRad));
                    float labelY = cy + (float)(radius * Math.Sin(labelAngleRad)) - 5; // Offset slightly above ring

                    string labelText = $"{rangeNm:F0} nm";
                    var textOptions = new RichTextOptions(font) {
                        Origin = new PointF(labelX, labelY),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };
                    // Use semi-transparent red color (alpha = 180/255 ≈ 70% opacity)
                    var labelColor = new Color(new Rgba32(255, 0, 0, 180));
                    ctx.DrawText(textOptions, labelText, labelColor);
                }
            });
        } catch {
            // If font rendering fails, labels are skipped but rings are already drawn
        }
    }

    // Draw range rings on SVG image
    private static void DrawRangeRingsSVG(XElement svgRoot, XNamespace svgNs, float radarLat, float radarLon,
        EWRPolarScan.GridSpec gridSpec, int cellSize) {
        // Range rings in nautical miles
        float[] rangeRingsNm = [5f, 10f, 25f, 50f];

        // Convert nautical miles to meters (1 nautical mile = 1852 meters)
        const float nmToMeters = 1852f;
        // Approximate degrees per meter: 1 degree latitude ≈ 111,000 meters
        const float metersPerDegreeLat = 111000f;

        // Calculate radar center position in pixels (scaled by cellSize)
        float centerX = (radarLon - gridSpec.LonMin) / gridSpec.LonRes * cellSize;
        float centerY = (gridSpec.LatMax - radarLat) / gridSpec.LatRes * cellSize; // y=0 is top (north)

        foreach (float rangeNm in rangeRingsNm) {
            float rangeMeters = rangeNm * nmToMeters;
            // Convert to degrees (use latitude for approximation, longitude will be similar at mid-latitudes)
            float rangeDegreesLat = rangeMeters / metersPerDegreeLat;
            float rangeDegreesLon = rangeMeters / (metersPerDegreeLat * MathF.Cos(radarLat * MathF.PI / 180f));

            // Calculate radius in pixels (scaled by cellSize)
            float radiusPixelsLat = (rangeDegreesLat / gridSpec.LatRes) * cellSize;
            float radiusPixelsLon = (rangeDegreesLon / gridSpec.LonRes) * cellSize;
            float radiusPixels = (radiusPixelsLat + radiusPixelsLon) / 2f;

            // Create circle element with thin, semi-transparent stroke
            XElement circle = new(
                svgNs + "circle",
                new XAttribute("cx", centerX),
                new XAttribute("cy", centerY),
                new XAttribute("r", radiusPixels),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", "red"),
                new XAttribute("stroke-width", "1"),
                new XAttribute("stroke-opacity", "0.7")
            );
            svgRoot.Add(circle);

            // Add label at the top of the ring (north, angle = -90 degrees)
            double labelAngleRad = -90.0 * Math.PI / 180.0; // Top (north)
            float labelX = centerX + (float)(radiusPixels * Math.Cos(labelAngleRad));
            float labelY = centerY + (float)(radiusPixels * Math.Sin(labelAngleRad)) - 5; // Offset slightly above ring

            string labelText = $"{rangeNm:F0} nm";
            XElement text = new(
                svgNs + "text",
                new XAttribute("x", labelX),
                new XAttribute("y", labelY),
                new XAttribute("fill", "red"),
                new XAttribute("fill-opacity", "0.7"),
                new XAttribute("font-size", "10"),
                new XAttribute("font-weight", "bold"),
                new XAttribute("text-anchor", "middle"),
                new XAttribute("dominant-baseline", "bottom"),
                labelText
            );
            svgRoot.Add(text);
        }
    }

    // Find min/max values in the data array (ignoring NaNs and NoData)
    private static (float min, float max, float range) GetNormalizationRange(float[,] data) {
        int height = data.GetLength(0);
        int width = data.GetLength(1);

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float v = data[y, x];
                if (float.IsNaN(v) || v == EWRPolarScan.NoData) continue;  // ignore NaNs and NoData

                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        float range = max - min;
        if (range == 0) range = 1f;
        return (min, max, range);
    }
    public void Save(string filePath, float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        Save(filePath, null, radarLat, radarLon, gridSpec);
    }

    /// <summary>
    /// Saves the image using standard thresholds for the specified channel.
    /// </summary>
    public void Save(string filePath, Channel? channel, float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        if (filePath.EndsWith(".png")) {
            if (channel.HasValue) {
                SavePNG(filePath, channel.Value, true, radarLat, radarLon, gridSpec);
            } else {
                SavePNG(filePath, true, radarLat, radarLon, gridSpec);
            }
        } else if (filePath.EndsWith(".svg")) {
            if (channel.HasValue) {
                SaveSVG(filePath, channel.Value, 5, true, radarLat, radarLon, gridSpec);
            } else {
                SaveSVG(filePath, 5, true, radarLat, radarLon, gridSpec);
            }
        } else {
            throw new ArgumentException("Invalid file extension. Must be .png or .svg.");
        }
    }

    public void SavePNG(string filePath, bool includeColorBar = true,
        float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        SavePNG(filePath, null, null, includeColorBar, radarLat, radarLon, gridSpec);
    }

    /// <summary>
    /// Saves the image as PNG using standard thresholds for the specified channel.
    /// </summary>
    public void SavePNG(string filePath, Channel channel, bool includeColorBar = true,
        float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        var threshold = ImageThreshold.Get(channel);
        SavePNG(filePath, threshold.ValueMin, threshold.ValueMax, includeColorBar, radarLat, radarLon, gridSpec);
    }

    /// <summary>
    /// Saves the image as PNG with explicit min/max values for normalization.
    /// </summary>
    public void SavePNG(string filePath, float? minValue, float? maxValue, bool includeColorBar = true,
        float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        float min, max, range;

        if (minValue.HasValue && maxValue.HasValue) {
            min = minValue.Value;
            max = maxValue.Value;
            range = max - min;
            if (range == 0) range = 1f;
        } else {
            var normalization = GetNormalizationRange(Data);
            min = normalization.min;
            max = normalization.max;
            range = normalization.range;
        }

        const int colorBarWidth = 60;
        const int padding = 10;
        const int borderWidth = 2;
        const int tickLength = 8;
        int totalWidth = Width + (includeColorBar ? colorBarWidth + padding + 2 * borderWidth + tickLength : 0);

        using var image = new Image<Rgba32>(totalWidth, Height);

        // Draw main image
        var black = new Rgba32(0, 0, 0, 255); // Black for nodata (outside radar bounds)
        var transparent = new Rgba32(0, 0, 0, 0); // Transparent for NaN values
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                float val = Data[y, x];
                if (float.IsNaN(val)) {
                    image[x, y] = transparent; // NaN values are transparent
                                               // } else if (val == EWRPolarScan.NoData) {
                                               // if closest to NoData, render as black
                } else if (Math.Abs(val - EWRPolarScan.NoData) < 1e-6) {
                    image[x, y] = black; // NoData (-9999f) renders as black
                } else {
                    float t = (val - min) / range;
                    image[x, y] = JetColor(t);
                }
            }
        }

        // Draw range rings if radar location and grid spec are provided
        if (radarLat.HasValue && radarLon.HasValue && gridSpec.HasValue) {
            DrawRangeRingsPNG(image, radarLat.Value, radarLon.Value, gridSpec.Value, Width, Height);
        }

        // Draw color bar
        if (includeColorBar) {
            int barX = Width + padding;
            int barHeight = Height - (2 * padding);
            int barY = padding;
            const int tickCount = 10;

            // Draw gradient bar
            for (int y = 0; y < barHeight; y++) {
                float t = 1f - ((float)y / barHeight); // Reverse so max is at top
                Rgba32 color = JetColor(t);
                for (int x = 0; x < colorBarWidth; x++) {
                    image[barX + x, barY + y] = color;
                }
            }

            // Draw white border around color bar
            var white = new Rgba32(255, 255, 255, 255);
            // Top border
            for (int x = barX - borderWidth; x < barX + colorBarWidth + borderWidth; x++) {
                for (int b = 0; b < borderWidth; b++) {
                    if (x >= 0 && x < totalWidth && barY - borderWidth + b >= 0 && barY - borderWidth + b < Height) {
                        image[x, barY - borderWidth + b] = white;
                    }
                }
            }
            // Bottom border
            for (int x = barX - borderWidth; x < barX + colorBarWidth + borderWidth; x++) {
                for (int b = 0; b < borderWidth; b++) {
                    if (x >= 0 && x < totalWidth && barY + barHeight + b >= 0 && barY + barHeight + b < Height) {
                        image[x, barY + barHeight + b] = white;
                    }
                }
            }
            // Left border
            for (int y = barY - borderWidth; y < barY + barHeight + borderWidth; y++) {
                for (int b = 0; b < borderWidth; b++) {
                    if (barX - borderWidth + b >= 0 && barX - borderWidth + b < totalWidth && y >= 0 && y < Height) {
                        image[barX - borderWidth + b, y] = white;
                    }
                }
            }
            // Right border
            for (int y = barY - borderWidth; y < barY + barHeight + borderWidth; y++) {
                for (int b = 0; b < borderWidth; b++) {
                    if (barX + colorBarWidth + b >= 0 && barX + colorBarWidth + b < totalWidth && y >= 0 && y < Height) {
                        image[barX + colorBarWidth + b, y] = white;
                    }
                }
            }

            // Draw tick marks and labels
            for (int i = 0; i <= tickCount; i++) {
                float t = 1f - ((float)i / tickCount); // From max (top) to min (bottom)
                float value = min + t * range;
                int yPos = barY + (int)((1f - t) * barHeight);

                // Draw tick mark (horizontal line extending to the right)
                int tickX = barX + colorBarWidth;
                for (int x = 0; x < tickLength; x++) {
                    if (tickX + x < totalWidth && yPos >= 0 && yPos < Height) {
                        image[tickX + x, yPos] = white;
                    }
                }
            }

            // Add text labels centered in the color bar
            try {
                var fontCollection = new FontCollection();
                fontCollection.AddSystemFonts();
                FontFamily fontFamily;
                if (fontCollection.Families.Any()) {
                    fontFamily = fontCollection.Families.First();
                } else {
                    fontFamily = fontCollection.Add("Arial");
                }
                var font = fontFamily.CreateFont(11, FontStyle.Bold);

                image.Mutate(ctx => {
                    for (int i = 0; i <= tickCount; i++) {
                        float t = 1f - ((float)i / tickCount);
                        float value = min + t * range;
                        float yPos = barY + (1f - t) * barHeight;

                        string valueText = value.ToString("F1");
                        var textOptions = new RichTextOptions(font) {
                            Origin = new PointF(barX + colorBarWidth / 2f, yPos),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        ctx.DrawText(textOptions, valueText, Color.White);
                    }
                });
            } catch {
                // If font rendering fails, skip text labels (tick marks will still be visible)
            }
        }

        image.Save(filePath);
    }



    public void SaveSVG(string filePath, int cellSize = 5, bool includeColorBar = true,
        float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        SaveSVG(filePath, cellSize, null, null, includeColorBar, radarLat, radarLon, gridSpec);
    }

    /// <summary>
    /// Saves the image as SVG using standard thresholds for the specified channel.
    /// </summary>
    public void SaveSVG(string filePath, Channel channel, int cellSize = 5, bool includeColorBar = true,
        float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        var threshold = ImageThreshold.Get(channel);
        SaveSVG(filePath, cellSize, threshold.ValueMin, threshold.ValueMax, includeColorBar, radarLat, radarLon, gridSpec);
    }

    /// <summary>
    /// Saves the image as SVG with explicit min/max values for normalization.
    /// </summary>
    public void SaveSVG(string filePath, int cellSize, float? minValue, float? maxValue, bool includeColorBar = true,
        float? radarLat = null, float? radarLon = null, EWRPolarScan.GridSpec? gridSpec = null) {
        // the purpose of the cellSize is to scale the image to the desired size
        float min, max, range;

        if (minValue.HasValue && maxValue.HasValue) {
            min = minValue.Value;
            max = maxValue.Value;
            range = max - min;
            if (range == 0) range = 1f;
        } else {
            var normalization = GetNormalizationRange(Data);
            min = normalization.min;
            max = normalization.max;
            range = normalization.range;
        }

        const int colorBarWidth = 60;
        const int padding = 10;
        const int tickLength = 8;
        int totalWidth = Width * cellSize + (includeColorBar ? colorBarWidth + padding + tickLength : 0);
        int totalHeight = Height * cellSize;

        XNamespace svgNs = "http://www.w3.org/2000/svg";
        XDocument svgDoc = new(
            new XElement(svgNs + "svg",
                new XAttribute("width", totalWidth),
                new XAttribute("height", totalHeight),
                new XAttribute("xmlns", svgNs)
            )
        );

        XElement svgRoot = svgDoc.Root!;

        // Draw main image
        for (int row = 0; row < Height; row++) {
            for (int col = 0; col < Width; col++) {
                float val = Data[row, col];
                string fillColor;

                if (float.IsNaN(val)) {
                    fillColor = "transparent"; // NaN values are transparent
                } else if (Math.Abs(val - EWRPolarScan.NoData) < 1e-6) {
                    fillColor = "black"; // NoData (-9999f) renders as black
                } else {
                    float t = (val - min) / range;
                    Rgba32 color = JetColor(t);
                    fillColor = ColorToHex(color);
                }

                XElement rect = new(
                    svgNs + "rect",
                    new XAttribute("x", col * cellSize),
                    new XAttribute("y", row * cellSize),
                    new XAttribute("width", cellSize),
                    new XAttribute("height", cellSize),
                    new XAttribute("fill", fillColor),
                    new XAttribute("stroke", "black"),
                    new XAttribute("stroke-width", "1")
                );
                svgRoot.Add(rect);
            }
        }

        // Draw range rings if radar location and grid spec are provided
        if (radarLat.HasValue && radarLon.HasValue && gridSpec.HasValue) {
            DrawRangeRingsSVG(svgRoot, svgNs, radarLat.Value, radarLon.Value, gridSpec.Value, cellSize);
        }

        // Draw color bar
        if (includeColorBar) {
            int barX = Width * cellSize + padding;
            int barHeight = totalHeight - (2 * padding);
            int barY = padding;

            // Create linear gradient definition
            XElement defs = new(svgNs + "defs");
            XElement linearGradient = new(
                svgNs + "linearGradient",
                new XAttribute("id", "colorBarGradient"),
                new XAttribute("x1", "0%"),
                new XAttribute("y1", "0%"),
                new XAttribute("x2", "0%"),
                new XAttribute("y2", "100%")
            );

            // Add gradient stops (from max at top to min at bottom)
            const int gradientSteps = 100;
            for (int i = 0; i <= gradientSteps; i++) {
                float t = 1f - ((float)i / gradientSteps);
                Rgba32 color = JetColor(t);
                string colorHex = ColorToHex(color);
                float offset = (float)i / gradientSteps * 100f;

                XElement stop = new(
                    svgNs + "stop",
                    new XAttribute("offset", $"{offset:F1}%"),
                    new XAttribute("stop-color", colorHex)
                );
                linearGradient.Add(stop);
            }

            defs.Add(linearGradient);
            svgRoot.Add(defs);

            // Draw color bar rectangle with gradient
            XElement colorBarRect = new(
                svgNs + "rect",
                new XAttribute("x", barX),
                new XAttribute("y", barY),
                new XAttribute("width", colorBarWidth),
                new XAttribute("height", barHeight),
                new XAttribute("fill", "url(#colorBarGradient)"),
                new XAttribute("stroke", "white"),
                new XAttribute("stroke-width", "2")
            );
            svgRoot.Add(colorBarRect);

            // Add tick marks and labels
            const int tickCount = 10;

            for (int i = 0; i <= tickCount; i++) {
                float t = 1f - ((float)i / tickCount); // From max (top) to min (bottom)
                float value = min + t * range;
                float yPos = barY + (1f - t) * barHeight;

                // Draw tick mark (horizontal line extending to the right)
                XElement tickLine = new(
                    svgNs + "line",
                    new XAttribute("x1", barX + colorBarWidth),
                    new XAttribute("y1", yPos),
                    new XAttribute("x2", barX + colorBarWidth + tickLength),
                    new XAttribute("y2", yPos),
                    new XAttribute("stroke", "white"),
                    new XAttribute("stroke-width", "2")
                );
                svgRoot.Add(tickLine);

                // Add text label centered in the middle of the color bar
                string valueText = value.ToString("F1");
                XElement labelText = new(
                    svgNs + "text",
                    new XAttribute("x", barX + colorBarWidth / 2),
                    new XAttribute("y", yPos + 4), // +4 to align with tick mark center
                    new XAttribute("text-anchor", "middle"),
                    new XAttribute("fill", "white"),
                    new XAttribute("font-family", "Arial, sans-serif"),
                    new XAttribute("font-size", "11"),
                    new XAttribute("font-weight", "bold"),
                    valueText
                );
                svgRoot.Add(labelText);
            }
        }

        svgDoc.Save(filePath);
    }
}

