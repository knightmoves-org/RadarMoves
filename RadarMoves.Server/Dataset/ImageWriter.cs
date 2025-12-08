using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Xml.Linq;

namespace RadarMoves.Server.Dataset;
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

    // Find min/max values in the data array (ignoring NaNs)
    private static (float min, float max, float range) GetNormalizationRange(float[,] data) {
        int height = data.GetLength(0);
        int width = data.GetLength(1);

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float v = data[y, x];
                if (float.IsNaN(v)) continue;  // ignore NaNs
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        float range = max - min;
        if (range == 0) range = 1f;
        return (min, max, range);
    }
    public void Save(string filePath) {
        if (filePath.EndsWith(".png")) {
            SavePNG(filePath);
        } else if (filePath.EndsWith(".svg")) {
            SaveSVG(filePath);
        } else {
            throw new ArgumentException("Invalid file extension. Must be .png or .svg.");
        }
    }

    public void SavePNG(string filePath) {
        var (min, max, range) = GetNormalizationRange(Data);

        using var image = new Image<Rgba32>(Width, Height);

        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                float val = Data[y, x];
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

    // public void SaveSVG(string filePath, int cellSize = 20) {
    //     CreateSvgFrom2DArray(Data, filePath, cellSize);
    // }

    public void SaveSVG(string filePath, int cellSize = 20) {
        var (min, max, range) = GetNormalizationRange(Data);

        XNamespace svgNs = "http://www.w3.org/2000/svg";
        XDocument svgDoc = new(
            new XElement(svgNs + "svg",
                new XAttribute("width", Width * cellSize),
                new XAttribute("height", Height * cellSize),
                new XAttribute("xmlns", svgNs)
            )
        );

        XElement svgRoot = svgDoc.Root!;

        for (int row = 0; row < Height; row++) {
            for (int col = 0; col < Width; col++) {
                float val = data[row, col];
                string fillColor;

                if (float.IsNaN(val)) {
                    fillColor = "transparent";
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

        svgDoc.Save(filePath);
    }
}

