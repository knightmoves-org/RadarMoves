
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RadarMoves.Server.Data;

public class Median3RaysFilter : IRadarFilter<float> {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Median3(float a, float b, float c) {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return b;
    }

    public void Apply(Span2D<float> grid) {

        int nR = grid.Y;
        int nB = grid.X;

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
public class Median5BinsFilter : IRadarFilter<float> {
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

    public void Apply(Span2D<float> grid) {
        int nR = grid.Y;
        int nB = grid.X;

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
public class ThresholdFilter : IRadarFilter<float> {
    private readonly float _min;
    private readonly float _max;

    public ThresholdFilter(float min, float max) {
        _min = min;
        _max = max;
    }

    public void Apply(Span2D<float> grid) {
        int nR = grid.Y;
        int nB = grid.X;

        for (int r = 0; r < nR; r++)
            for (int b = 0; b < nB; b++) {
                var v = grid[r, b];
                if (v < _min || v > _max)
                    grid[r, b] = float.NaN;
            }
    }
}
public class GateClutterFilter : IRadarFilter<float> {
    private readonly int _window;
    private readonly float _stdThresh;

    public GateClutterFilter(int window = 7, float stdThresh = 3f) {
        _window = window;
        _stdThresh = stdThresh;
    }

    public void Apply(Span2D<float> grid) {

        int nR = grid.Y;
        int nB = grid.X;
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
public class SpeckleRemovalFilter(float threshold = 0.0f, int minArea = 5) : IRadarFilter<float> {
    private readonly float _threshold = threshold;
    private readonly int _minArea = minArea;

    public void Apply(Span2D<float> grid) {
        int nR = grid.Y;
        int nB = grid.X;

        bool[,] visited = new bool[nR, nB];
        int[] dr = [1, -1, 0, 0];
        int[] db = [0, 0, 1, -1];

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

    public void Apply2(Span2D<float> data) {
        int rows = data.Y;
        int cols = data.X;

        // Label grid same size as radar data
        int[,] labelsArr = new int[rows, cols];
        var labels = labelsArr.AsSpan();

        // Union-find parent table (worst case each pixel gets a label)
        int maxLabels = rows * cols / 2 + 1;
        int[] parent = new int[maxLabels];
        int nextLabel = 1;

        // ---------- FIRST PASS ----------
        for (int y = 0; y < rows; y++) {
            var row = data.GetRowSpan(y);
            var lblRow = labels.GetRowSpan(y);

            for (int x = 0; x < cols; x++) {
                if (row[x] < _threshold) {
                    // not foreground
                    lblRow[x] = 0;
                    continue;
                }

                int left = (x > 0) ? lblRow[x - 1] : 0;
                int up = (y > 0) ? labels[y - 1, x] : 0;

                if (left == 0 && up == 0) {
                    // New label
                    lblRow[x] = nextLabel;
                    parent[nextLabel] = nextLabel;
                    nextLabel++;
                } else if (left != 0 && up != 0) {
                    // Both labels exist -> unify
                    int l = Find(parent, left);
                    int u = Find(parent, up);

                    if (l != u)
                        Union(parent, l, u);

                    lblRow[x] = l;
                } else {
                    // One neighbor has label
                    lblRow[x] = left != 0 ? left : up;
                }
            }
        }

        // ---------- SECOND PASS ----------
        // Resolve label roots + count areas
        int[] area = new int[nextLabel + 1];

        for (int y = 0; y < rows; y++) {
            var lbl = labels.GetRowSpan(y);
            for (int x = 0; x < cols; x++) {
                int v = lbl[x];
                if (v != 0) {
                    int root = Find(parent, v);
                    lbl[x] = root;
                    area[root]++;
                }
            }
        }

        // ---------- THIRD PASS ----------
        // Remove tiny speckles
        for (int y = 0; y < rows; y++) {
            var row = data.GetRowSpan(y);
            var lbl = labels.GetRowSpan(y);

            for (int x = 0; x < cols; x++) {
                int id = lbl[x];
                if (id != 0 && area[id] < _minArea)
                    row[x] = float.NaN; // or row[x] = _threshold - 1f;
            }
        }
    }

    // Union-Find helpers (very small, very fast)
    private static int Find(int[] parent, int x) {
        while (parent[x] != x) {
            parent[x] = parent[parent[x]]; // path compression
            x = parent[x];
        }
        return x;
    }

    private static void Union(int[] parent, int a, int b) {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra != rb)
            parent[rb] = ra;
    }
}

public class GaussianFilter : IRadarFilter<float> {
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

    public void Apply(Span2D<float> grid) {
        int nR = grid.Y;
        int nB = grid.X;
        int total = nR * nB;

        float[] tmp = ArrayPool<float>.Shared.Rent(total);
        var tmpGrid = new Span2D<float>(tmp.AsSpan(0, total), nR, nB);

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
    public void ApplyOld(Span2D<float> data) {
        var (rows, cols) = data.Shape();

        // Temp buffer for vertical pass
        float[,] temp = new float[rows, cols];
        var temp2D = temp.AsSpan();

        HorizontalPass(data, temp2D);
        VerticalPass(temp2D, data);
    }

    private void HorizontalPass(Span2D<float> input, Span2D<float> output) {
        var (rows, cols) = input.Shape();
        int vSize = Vector<float>.Count;

        for (int y = 0; y < rows; y++) {
            var rowIn = input.GetRowSpan(y);
            var rowOut = output.GetRowSpan(y);

            for (int x = 0; x < cols; x++) {
                Vector<float> sumVec = Vector<float>.Zero;
                float sumScalar = 0f;

                // Convolve
                for (int k = -_radius; k <= _radius; k++) {
                    int xx = x + k;
                    if ((uint)xx >= (uint)cols)
                        continue;

                    float w = _kernel[k + _radius];

                    int remaining = cols - xx;
                    if (remaining >= vSize) {
                        var v = new Vector<float>(rowIn.Slice(xx));
                        sumVec += v * new Vector<float>(w);
                    } else {
                        sumScalar += rowIn[xx] * w;
                    }
                }

                float total = sumScalar;
                for (int i = 0; i < vSize; i++)
                    total += sumVec[i];

                rowOut[x] = total;
            }
        }
    }

    private void VerticalPass(Span2D<float> input, Span2D<float> output) {
        var (rows, cols) = input.Shape();

        for (int x = 0; x < cols; x++) {
            for (int y = 0; y < rows; y++) {
                float accum = 0f;

                for (int k = -_radius; k <= _radius; k++) {
                    int yy = y + k;
                    if ((uint)yy >= (uint)rows)
                        continue;

                    float w = _kernel[k + _radius];
                    accum += input[yy, x] * w;
                }

                output[y, x] = accum;
            }
        }
    }
}
