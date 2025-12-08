
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RadarMoves.Server.Dataset;
public class ImageWriter(float[,] data) {
    public readonly float[,] Data = data;
    // Simple Jet-style colormap (blue → green → yellow → red)
    private static Rgba32 JetColor(float t) {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 3f), 0f, 1f) * 255);
        byte g = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 2f), 0f, 1f) * 255);
        byte b = (byte)(Math.Clamp(1.5f - Math.Abs(4f * t - 1f), 0f, 1f) * 255);
        return new Rgba32(r, g, b);
    }

    public void Save(string filePath) {
        int height = Data.GetLength(0);
        int width = Data.GetLength(1);

        // Find min/max for normalization
        float min = float.MaxValue;
        float max = float.MinValue;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float v = Data[y, x];
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
}
