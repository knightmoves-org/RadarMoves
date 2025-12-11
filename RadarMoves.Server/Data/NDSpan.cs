using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RadarMoves.Server.Data;
/// <summary>
/// N-dimensional span over a flat contiguous buffer.
/// - ref struct (stack-only)
/// - zero-copy slicing (returns new NDSpan with same underlying buffer)
/// - fast O(1) indexing (precomputed strides)
/// </summary>
public readonly ref struct NDSpan<T> where T : unmanaged {
    private readonly Span<T> _data;      // underlying flat buffer
    private readonly int[] _shape;       // per-dimension length
    private readonly int[] _strides;     // per-dimension stride (in elements)
    private readonly int _offset;        // offset into _data where this view starts

    #region Constructors / Factories

    /// <summary>
    /// Construct from an existing flat buffer and explicit shape.
    /// </summary>
    public NDSpan(Span<T> buffer, int[] shape) : this(buffer, shape, ComputeStrides(shape), 0) { }

    /// <summary>
    /// Internal constructor (allows slice to set offset/strides).
    /// </summary>
    private NDSpan(Span<T> buffer, int[] shape, int[] strides, int offset) {
        _data = buffer;
        _shape = shape;
        _strides = strides;
        _offset = offset;
    }

    /// <summary>
    /// Flatten any Array (T[], T[,], T[,,], ...) into a contiguous buffer and return NDSpan.
    /// </summary>
    public static NDSpan<T> FromArray(Array arr) {
        ArgumentNullException.ThrowIfNull(arr);
        int rank = arr.Rank;
        if (rank == 0) throw new ArgumentException("Array must have rank >= 1", nameof(arr));

        var shape = new int[rank];
        int total = 1;
        for (int d = 0; d < rank; d++) {
            shape[d] = arr.GetLength(d);
            total *= shape[d];
        }

        var buf = new T[total];
        int idx = 0;
        foreach (object? v in arr) // enumerates in row-major order
            buf[idx++] = (T)v!;


        return new NDSpan<T>(buf.AsSpan(), shape);
    }

    /// <summary>
    /// Construct directly from a flat T[] and shape (fast; no copy).
    /// </summary>
    public static NDSpan<T> FromFlatArray(T[] flatBuffer, int[] shape) {
        ArgumentNullException.ThrowIfNull(flatBuffer);
        return new NDSpan<T>(flatBuffer.AsSpan(), shape);
    }

    #endregion

    #region Properties / Info

    public int Rank => _shape.Length;

    public ReadOnlySpan<int> Shape => _shape;

    public ReadOnlySpan<int> Strides => _strides;

    public int Length {
        get {
            int len = 1;
            for (int i = 0; i < _shape.Length; i++) len *= _shape[i];
            return len;
        }
    }

    /// <summary>Return the span representing the contiguous block backing this view (may include offset).</summary>
    public Span<T> AsSpan() {
        // The storage for a sliced NDSpan may still be contiguous only if it represents
        // the full underlying block or a simple sub-block; we expose the underlying slice
        int len = Length;
        return _data.Slice(_offset, len);
    }

    #endregion

    #region Indexing (integer indices)

    /// <summary>
    /// Fast ref-indexer with integer indices: nd[i0, i1, ..., iN]
    /// </summary>
    public ref T this[params int[] indices] {
        get {
            ArgumentNullException.ThrowIfNull(indices);
            if (indices.Length != _shape.Length) throw new ArgumentException($"Index rank ({indices.Length}) does not match NDSpan rank ({_shape.Length}).");

            int flat = _offset;
#if DEBUG
            // bounds checking only in DEBUG for safety; release will be faster.
            for (int d = 0; d < indices.Length; d++) {
                int idx = indices[d];
                if ((uint)idx >= (uint)_shape[d]) throw new IndexOutOfRangeException($"Index {d} out of range: {idx} (shape {_shape[d]})");
                flat += idx * _strides[d];
            }
#else
                for (int d = 0; d < indices.Length; d++)
                    flat += indices[d] * _strides[d];
#endif
            return ref _data[flat];
        }
    }

    #endregion

    #region Slicing (Range[])

    /// <summary>
    /// Slice with ranges for each axis. Missing ranges (fewer passed than Rank) are treated as full (..).
    /// Example: nd[.., 3..5, ..]
    /// Returns a new NDSpan<T> view (zero-copy).
    /// </summary>
    public NDSpan<T> this[params Range[] ranges] {
        get {
            // Normalize ranges to full dimensionality
            Range[] r = NormalizeRanges(ranges, Rank);

            // Compute new shape and offset
            var newShape = new int[Rank];
            int newOffset = _offset;
            for (int d = 0; d < Rank; d++) {
                var length = _shape[d];
                var (start, len) = r[d].GetOffsetAndLength(length);
                newShape[d] = len;
                newOffset += start * _strides[d];
            }

            return new NDSpan<T>(_data, newShape, _strides, newOffset);
        }
    }

    /// <summary>
    /// Normalize incoming ranges: if fewer provided than rank, pad with full-range (..).
    /// </summary>
    private static Range[] NormalizeRanges(Range[] ranges, int rank) {
        if (ranges == null) return Enumerable.Repeat(Range.All, rank).ToArray();
        if (ranges.Length == rank) return ranges;

        var outRanges = new Range[rank];
        for (int i = 0; i < rank; i++) {
            outRanges[i] = i < ranges.Length ? ranges[i] : Range.All;
        }
        return outRanges;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Compute strides for row-major layout.
    /// stride[i] is number of elements to skip to advance index in dimension i by 1.
    /// </summary>
    private static int[] ComputeStrides(int[] shape) {
        int d = shape.Length;
        var strides = new int[d];
        int s = 1;
        for (int i = d - 1; i >= 0; i--) {
            strides[i] = s;
            s *= shape[i];
        }
        return strides;
    }

    /// <summary>
    /// Return a 1D span for a specific axis fixed by indices for preceding axes.
    /// Example: Get1DSpan(new[]{k, i}, axis = 2) => span along axis 2 with k,i fixed.
    /// Useful for SIMD processing of the innermost dimension.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> Get1DSpan(int axis, params int[] fixedIndices) {
        if (axis < 0 || axis >= Rank) throw new ArgumentOutOfRangeException(nameof(axis));
        ArgumentNullException.ThrowIfNull(fixedIndices);
        if (fixedIndices.Length != Rank - 1) throw new ArgumentException($"fixedIndices must have Rank-1 elements (expected {Rank - 1})");

        // compute offset within this view
        int off = _offset;
        int fi = 0;
        for (int d = 0; d < Rank; d++) {
            if (d == axis) continue;
            int idx = fixedIndices[fi++];
#if DEBUG
            if ((uint)idx >= (uint)_shape[d]) throw new IndexOutOfRangeException($"Index {d} out of range: {idx}");
#endif
            off += idx * _strides[d];
        }

        // size along axis
        int len = _shape[axis];
        return _data.Slice(off, len);
    }

    #endregion
}

/// <summary>
/// Convenience extension methods for creating NDSpan from arrays or flat buffers.
/// </summary>
public static class NDSpanExtensions {
    /// <summary>Wrap a flat T[] with shape (no copy).</summary>
    public static NDSpan<T> AsNDSpan<T>(this T[] flatBuffer, params int[] shape) where T : unmanaged {
        ArgumentNullException.ThrowIfNull(flatBuffer);
        if (shape == null || shape.Length == 0) throw new ArgumentException("shape must be non-empty", nameof(shape));
        int prod = 1;
        for (int i = 0; i < shape.Length; i++) prod *= shape[i];
        if (prod != flatBuffer.Length) throw new ArgumentException($"Product of shape ({prod}) must equal flatBuffer.Length ({flatBuffer.Length})");

        return NDSpan<T>.FromFlatArray(flatBuffer, shape);
    }

    /// <summary>Flatten any multi-rank Array to an NDSpan (copies the data).</summary>
    public static NDSpan<T> AsNDSpan<T>(this Array arr) where T : unmanaged {
        ArgumentNullException.ThrowIfNull(arr);
        return NDSpan<T>.FromArray(arr);
    }
}


