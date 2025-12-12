using System.Collections;
using System.Numerics;

namespace RadarMoves.Server.Data;

public readonly ref struct Span2D<T>(Span<T> data, int y, int x) where T : unmanaged {
    #region Fields
    private readonly Span<T> _data = data;
    public readonly int Y = y;
    public readonly int X = x;
    public readonly int Stride = x;
    #endregion
    public (int Y, int X) Shape() => (Y, X);

    public int Shape(int axis) => axis switch {
        0 => Y,
        1 => X,
        _ => throw new ArgumentException($"Dimension must be 0 or 1, got {axis}")
    };

    #region Static Class Constructors

    public static Span2D<T> FromArray(T[,] data, int y, int x) {
        var buf = new T[y * x];
        for (int i = 0; i < y; i++)
            for (int j = 0; j < x; j++)
                buf[i * x + j] = data[i, j];

        return new Span2D<T>(buf.AsSpan(), y, x);
    }
    public static Span2D<T> FromArray(T[,] data, (int y, int x) shape) => FromArray(data, shape.y, shape.x);
    public static Span2D<T> FromArray(T[,] data) => FromArray(data, data.GetLength(0), data.GetLength(1));

    #endregion

    public ref T this[int i, int j] => ref _data[i * X + j];

    public T[,] AsArray() {
        var arr = new T[Y, X];
        for (int i = 0; i < Y; i++)
            for (int j = 0; j < X; j++)
                arr[i, j] = _data[i * X + j];

        return arr;
    }
    public Span<T> GetRowSpan(int y) => _data.Slice(y * X, X);
    public Span<T> GetColumnSpan(int x) => _data.Slice(x, Y * X);
    public IEnumerator GetEnumerator() => AsArray().GetEnumerator();

}
public static class ArrayExtensions {
    #region Fill
    public static void Fill<T>(this T[,] arr, T value, int y, int x) where T : unmanaged {
        for (int i = 0; i < y; i++) {
            for (int j = 0; j < x; j++) {
                arr[i, j] = value;
            }
        }
    }

    #endregion
    public static void Fill<T>(this T[,] arr, T value, (int y, int x) shape) where T : unmanaged => arr.Fill(value, shape.y, shape.x);
    public static void Fill<T>(this T[,] arr, T value) where T : unmanaged => arr.Fill(value, arr.GetLength(0), arr.GetLength(1));
    #region Flatten2D
    public static T[] Flatten<T>(this T[,] arr, int y, int x) where T : unmanaged {
        var buf = new T[y * x];
        for (int i = 0; i < y; i++)
            for (int j = 0; j < x; j++)
                buf[i * x + j] = arr[i, j];

        return buf;
    }
    public static T[] Flatten<T>(this T[,] arr, (int y, int x) shape) where T : unmanaged => arr.Flatten(shape.y, shape.x);
    public static T[] Flatten<T>(this T[,] arr) where T : unmanaged => arr.Flatten(arr.GetLength(0), arr.GetLength(1));
    #endregion
    #region Flatten3D
    public static T[] Flatten<T>(this T[,,] arr, int z, int y, int x) where T : unmanaged {
        var buf = new T[z * y * x];
        for (int k = 0; k < z; k++)
            for (int i = 0; i < y; i++)
                for (int j = 0; j < x; j++)
                    buf[(k * y * x) + (i * x) + j] = arr[k, i, j];

        return buf;
    }
    public static T[] Flatten<T>(this T[,,] arr, (int z, int y, int x) shape) where T : unmanaged => arr.Flatten(shape.z, shape.y, shape.x);
    public static T[] Flatten<T>(this T[,,] arr) where T : unmanaged => arr.Flatten(arr.GetLength(0), arr.GetLength(1), arr.GetLength(2));
    #endregion

    public static Span2D<T> AsSpan<T>(this T[,] arr) where T : unmanaged {
        int y = arr.GetLength(0);
        int x = arr.GetLength(1);
        return new Span2D<T>(arr.Flatten(y, x).AsSpan(), y, x);
    }

    public static Vector<T> AsVector<T>(this T[,] arr) where T : unmanaged {
        int y = arr.GetLength(0);
        int x = arr.GetLength(1);
        return new Vector<T>(arr.Flatten(y, x));
    }

}

