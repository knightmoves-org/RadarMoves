using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace RadarMoves.Server.Dataset;

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

    // Find min/max values in the data array (ignoring NaNs and NoDataValue)
    private static (float min, float max, float range) GetNormalizationRange(float[,] data) {
        int height = data.GetLength(0);
        int width = data.GetLength(1);

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float v = data[y, x];
                if (float.IsNaN(v) || v == EWRPolarScan.NoDataValue) continue;  // ignore NaNs and NoDataValue

                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        float range = max - min;
        if (range == 0) range = 1f;
        return (min, max, range);
    }
    public void Save(string filePath) {
        Save(filePath, null);
    }

    /// <summary>
    /// Saves the image using standard thresholds for the specified channel.
    /// </summary>
    public void Save(string filePath, Channel? channel) {
        if (filePath.EndsWith(".png")) {
            if (channel.HasValue) {
                SavePNG(filePath, channel.Value);
            } else {
                SavePNG(filePath);
            }
        } else if (filePath.EndsWith(".svg")) {
            if (channel.HasValue) {
                SaveSVG(filePath, channel.Value);
            } else {
                SaveSVG(filePath);
            }
        } else {
            throw new ArgumentException("Invalid file extension. Must be .png or .svg.");
        }
    }

    public void SavePNG(string filePath, bool includeColorBar = true) {
        SavePNG(filePath, null, null, includeColorBar);
    }

    /// <summary>
    /// Saves the image as PNG using standard thresholds for the specified channel.
    /// </summary>
    public void SavePNG(string filePath, Channel channel, bool includeColorBar = true) {
        var threshold = ImageThreshold.Get(channel);
        SavePNG(filePath, threshold.ValueMin, threshold.ValueMax, includeColorBar);
    }

    /// <summary>
    /// Saves the image as PNG with explicit min/max values for normalization.
    /// </summary>
    public void SavePNG(string filePath, float? minValue, float? maxValue, bool includeColorBar = true) {
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
                                               // } else if (val == EWRPolarScan.NoDataValue) {
                                               // if closest to NoDataValue, render as black
                } else if (Math.Abs(val - EWRPolarScan.NoDataValue) < 1e-6) {
                    image[x, y] = black; // NoDataValue (-9999f) renders as black
                } else {
                    float t = (val - min) / range;
                    image[x, y] = JetColor(t);
                }
            }
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



    public void SaveSVG(string filePath, int cellSize = 5, bool includeColorBar = true) {
        SaveSVG(filePath, cellSize, null, null, includeColorBar);
    }

    /// <summary>
    /// Saves the image as SVG using standard thresholds for the specified channel.
    /// </summary>
    public void SaveSVG(string filePath, Channel channel, int cellSize = 5, bool includeColorBar = true) {
        var threshold = ImageThreshold.Get(channel);
        SaveSVG(filePath, cellSize, threshold.ValueMin, threshold.ValueMax, includeColorBar);
    }

    /// <summary>
    /// Saves the image as SVG with explicit min/max values for normalization.
    /// </summary>
    public void SaveSVG(string filePath, int cellSize, float? minValue, float? maxValue, bool includeColorBar = true) {
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
                } else if (Math.Abs(val - EWRPolarScan.NoDataValue) < 1e-6) {
                    fillColor = "black"; // NoDataValue (-9999f) renders as black
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

