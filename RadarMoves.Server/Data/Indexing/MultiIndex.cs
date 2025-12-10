using System.Collections;
using System.Runtime.InteropServices;
using System.Linq;
using System.Runtime.CompilerServices;


namespace RadarMoves.Server.Data.Indexing;

/// <summary>
/// Represents a multi-level index structure similar to pandas MultiIndex.
/// Can work with tuples of any size, e.g., MultiIndex<(int, int)> or MultiIndex<(DateTime, float, int)>.
/// </summary>
public readonly struct MultiIndex<T>(T[] values) : IEquatable<MultiIndex<T>>, IComparable<MultiIndex<T>>, IEnumerable<object?>
    where T : struct, ITuple {
    private readonly T[] _values = values;

    public readonly int Length => _values.Length;

    public readonly T this[int index] => _values[index];

    public readonly bool Equals(MultiIndex<T> other) {
        if (_values.Length != other._values.Length) return false;
        for (int i = 0; i < _values.Length; i++) {
            var thisValue = _values[i];
            var otherValue = other._values[i];
            if (!Equals(thisValue, otherValue)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) {
        return obj is MultiIndex<T> other && Equals(other);
    }

    public override readonly int GetHashCode() {
        var hash = new HashCode();
        for (int i = 0; i < _values.Length; i++) {
            hash.Add(_values[i]);
        }
        return hash.ToHashCode();
    }

    public readonly int CompareTo(MultiIndex<T> other) {
        var minLength = Math.Min(_values.Length, other._values.Length);
        for (int i = 0; i < minLength; i++) {
            var thisValue = _values[i];
            var otherValue = other._values[i];

            // Try to compare as IComparable
            if (thisValue is IComparable thisComparable && otherValue is IComparable otherComparable) {
                var comparison = thisComparable.CompareTo(otherComparable);
                if (comparison != 0) return comparison;
            } else {
                // Fallback to string comparison if not comparable
                var thisStr = thisValue.ToString() ?? "";
                var otherStr = otherValue.ToString() ?? "";
                var strComparison = string.Compare(thisStr, otherStr, StringComparison.Ordinal);
                if (strComparison != 0) return strComparison;
            }
        }
        return _values.Length.CompareTo(other._values.Length);
    }

    public static bool operator ==(MultiIndex<T> left, MultiIndex<T> right) {
        return left.Equals(right);
    }

    public static bool operator !=(MultiIndex<T> left, MultiIndex<T> right) {
        return !(left == right);
    }

    public static bool operator <(MultiIndex<T> left, MultiIndex<T> right) {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(MultiIndex<T> left, MultiIndex<T> right) {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(MultiIndex<T> left, MultiIndex<T> right) {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(MultiIndex<T> left, MultiIndex<T> right) {
        return left.CompareTo(right) >= 0;
    }

    public override readonly string ToString() {
        var items = new List<string>();
        for (int i = 0; i < _values.Length; i++) {
            items.Add(_values[i].ToString() ?? "null");
        }
        return $"({string.Join(", ", items)})";
    }

    public IEnumerator<object?> GetEnumerator() {
        for (int i = 0; i < _values.Length; i++) {
            yield return _values[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
    public object?[] GetLevelValues(int level) {
        List<object?> result = [];
        foreach (var value in _values) {
            if (value is ITuple tuple && tuple.Length > level) {
                result.Add(tuple[level]);
            }
        }
        return result.ToArray()!;
    }
}



/// <summary>
/// Represents an indexed series structure similar to pandas Series.
/// Inherits from Dictionary to provide direct dictionary access while maintaining Series semantics.
/// </summary>
public class Series<T1, T2> : Dictionary<T1, T2> where T1 : struct, ITuple {
    public MultiIndex<T1> Index => new([.. Keys]);
    public Series() : base() { }

    public Series(Dictionary<T1, T2> data) : base(data) { }

    public Series(T2[] values, T1[] index) : base() {
        // values and index must have the same length
        if (values.Length != index.Length) {
            throw new ArgumentException("Values and index must have the same length");
        }
        for (int i = 0; i < values.Length; i++) {
            var key = index[i];
            var value = values[i];
            this[key] = value;
        }
    }

    public Series(IEnumerable<T2> values, IEnumerable<T1> index) : base() {
        var valuesList = values.ToList();
        var indexList = index.ToList();

        if (valuesList.Count != indexList.Count) {
            throw new ArgumentException("Values and index must have the same length");
        }

        for (int i = 0; i < indexList.Count; i++) {
            this[indexList[i]] = valuesList[i];
        }
    }

    public Series(IEnumerable<KeyValuePair<T1, T2>> items) : base(items) { }

    public Series<T1, T2> this[IEnumerable<T1> index] {
        get {
            var result = new Series<T1, T2>();
            foreach (var idx in index) {
                if (TryGetValue(idx, out var value)) {
                    result[idx] = value;
                }
            }
            return result;
        }
        set {
            foreach (var idx in index) {
                this[idx] = value[idx];
            }
        }
    }
}

// Example usage:
class Program {
    static void Main(string[] args) {
        MultiIndex<(int, int)> idx = new([(1, 2)]);
        Console.WriteLine(idx);
    }
}
// MultiIndex<(int, int, int)> idx3 = new((1, 2, 3));
// MultiIndex<(DateTime, float)> idxDateTime = new((DateTime.Now, 1.5f));
// MultiIndex<DateTime, float> idx2 = new(DateTime.Now, 1.5f); // Two-parameter version for backward compatibility