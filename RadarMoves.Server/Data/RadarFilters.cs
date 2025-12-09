
using System.Buffers;
using System.Runtime.CompilerServices;

namespace RadarMoves.Server.Data;

public class Median3RaysFilter : IRadarFilter {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Median3(float a, float b, float c) {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return b;
    }

    public void Apply(Span2D grid) {
        int nR = grid.NRays;
        int nB = grid.NBins;

        for (int b = 0; b < nB; b++) {
            for (int r = 0; r < nR; r++) {
                float p0 = grid[(r - 1 + nR) % nR, b];
                float p1 = grid[r, b];
                float p2 = grid[(r + 1) % nR, b];

                grid[r, b] = Median3(p0, p1, p2);
            }
        }
    }
}
public class Median5BinsFilter : IRadarFilter {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Median5(float a, float b, float c, float d, float e) {
        if (a > b) (a, b) = (b, a);
        if (d > e) (d, e) = (e, d);
        if (a > c) (a, c) = (c, a);
        if (b > c) (b, c) = (c, b);
        if (a > d) (a, d) = (d, a);
        if (c > d) (c, d) = (d, c);
        if (b > e) (b, e) = (e, b);
        if (b > c) (b, c) = (c, b);
        return c;
    }

    public void Apply(Span2D grid) {
        int nR = grid.NRays;
        int nB = grid.NBins;

        for (int r = 0; r < nR; r++) {
            for (int b = 2; b < nB - 2; b++) {
                float p0 = grid[r, b - 2];
                float p1 = grid[r, b - 1];
                float p2 = grid[r, b];
                float p3 = grid[r, b + 1];
                float p4 = grid[r, b + 2];

                grid[r, b] = Median5(p0, p1, p2, p3, p4);
            }
        }
    }
}
public class ThresholdFilter : IRadarFilter {
    private readonly float _min;
    private readonly float _max;

    public ThresholdFilter(float min, float max) {
        _min = min;
        _max = max;
    }

    public void Apply(Span2D grid) {
        int nR = grid.NRays;
        int nB = grid.NBins;

        for (int r = 0; r < nR; r++)
            for (int b = 0; b < nB; b++) {
                var v = grid[r, b];
                if (v < _min || v > _max)
                    grid[r, b] = float.NaN;
            }
    }
}
public class GateClutterFilter : IRadarFilter {
    private readonly int _window;
    private readonly float _stdThresh;

    public GateClutterFilter(int window = 7, float stdThresh = 3f) {
        _window = window;
        _stdThresh = stdThresh;
    }

    public void Apply(Span2D grid) {
        int nR = grid.NRays;
        int nB = grid.NBins;
        int r = _window / 2;

        for (int b = 0; b < nB; b++) {
            for (int i = 0; i < nR; i++) {
                float mean = 0, count = 0;

                for (int k = -r; k <= r; k++) {
                    float v = grid[(i + k + nR) % nR, b];
                    if (!float.IsNaN(v)) {
                        mean += v;
                        count++;
                    }
                }
                if (count < 2) continue;
                mean /= count;

                float var = 0;
                for (int k = -r; k <= r; k++) {
                    float v = grid[(i + k + nR) % nR, b];
                    if (!float.IsNaN(v))
                        var += (v - mean) * (v - mean);
                }

                float std = MathF.Sqrt(var / count);
                if (std > _stdThresh)
                    grid[i, b] = float.NaN;
            }
        }
    }
}
public class SpeckleRemovalFilter : IRadarFilter {
    private readonly int _minArea;

    public SpeckleRemovalFilter(int minArea = 5) {
        _minArea = minArea;
    }

    public void Apply(Span2D grid) {
        int nR = grid.NRays;
        int nB = grid.NBins;

        bool[,] visited = new bool[nR, nB];
        int[] dr = { 1, -1, 0, 0 };
        int[] db = { 0, 0, 1, -1 };

        for (int r = 0; r < nR; r++) {
            for (int b = 0; b < nB; b++) {
                if (visited[r, b] || float.IsNaN(grid[r, b])) continue;

                var queue = new Queue<(int, int)>();
                var comp = new List<(int, int)>();

                queue.Enqueue((r, b));
                visited[r, b] = true;

                while (queue.Count > 0) {
                    var (rr, bb) = queue.Dequeue();
                    comp.Add((rr, bb));

                    for (int k = 0; k < 4; k++) {
                        int nr = rr + dr[k];
                        int nb = bb + db[k];

                        if (nr < 0 || nr >= nR || nb < 0 || nb >= nB) continue;

                        if (!visited[nr, nb] && !float.IsNaN(grid[nr, nb])) {
                            visited[nr, nb] = true;
                            queue.Enqueue((nr, nb));
                        }
                    }
                }

                if (comp.Count < _minArea) {
                    foreach (var (rr, bb) in comp)
                        grid[rr, bb] = float.NaN;
                }
            }
        }
    }
}



public class GaussianFilter : IRadarFilter {
    private readonly float[] _kernel;
    private readonly int _radius;

    public GaussianFilter(float sigma = 1.2f, int radius = 3) {
        _radius = radius;
        _kernel = new float[2 * radius + 1];

        float sum = 0;
        for (int i = -radius; i <= radius; i++) {
            float v = MathF.Exp(-(i * i) / (2 * sigma * sigma));
            _kernel[i + radius] = v;
            sum += v;
        }
        for (int i = 0; i < _kernel.Length; i++)
            _kernel[i] /= sum;
    }

    public void Apply(Span2D grid) {
        int nR = grid.NRays;
        int nB = grid.NBins;
        int total = nR * nB;

        float[] tmp = ArrayPool<float>.Shared.Rent(total);
        var tmpGrid = new Span2D(tmp.AsSpan(0, total), nR, nB);

        // Horizontal pass
        for (int b = 0; b < nB; b++) {
            for (int r = 0; r < nR; r++) {
                float acc = 0;
                for (int k = -_radius; k <= _radius; k++) {
                    int rr = (r + k + nR) % nR;
                    acc += grid[rr, b] * _kernel[k + _radius];
                }
                tmpGrid[r, b] = acc;
            }
        }

        // Vertical pass
        for (int r = 0; r < nR; r++) {
            for (int b = 0; b < nB; b++) {
                float acc = 0;
                for (int k = -_radius; k <= _radius; k++) {
                    int bb = b + k;
                    if (bb < 0 || bb >= nB) continue;

                    acc += tmpGrid[r, bb] * _kernel[k + _radius];
                }
                grid[r, b] = acc;
            }
        }

        ArrayPool<float>.Shared.Return(tmp);
    }
}
