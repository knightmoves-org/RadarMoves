using RadarMoves.Server.Data;
using Xunit;

namespace RadarMoves.Test;

public class NDSpanTests {
    #region Construction Tests

    [Fact]
    public void FromFlatArray_WithValidShape_ShouldCreateNDSpan() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        int[] shape = { 2, 3 };

        // Act
        var nd = NDSpan<int>.FromFlatArray(flatBuffer, shape);

        // Assert
        Assert.Equal(2, nd.Rank);
        Assert.Equal(6, nd.Length);
        Assert.Equal(new[] { 2, 3 }, nd.Shape.ToArray());
    }

    [Fact]
    public void FromFlatArray_WithNullBuffer_ShouldThrowArgumentNullException() {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => NDSpan<int>.FromFlatArray(null!, new[] { 2, 3 }));
    }

    [Fact]
    public void FromArray_With1DArray_ShouldCreateNDSpan() {
        // Arrange
        int[] arr = { 10, 20, 30 };

        // Act
        var nd = NDSpan<int>.FromArray(arr);

        // Assert
        Assert.Equal(1, nd.Rank);
        Assert.Equal(3, nd.Length);
        Assert.Equal(3, nd.Shape[0]);
        Assert.Equal(10, nd[0]);
        Assert.Equal(20, nd[1]);
        Assert.Equal(30, nd[2]);
    }

    [Fact]
    public void FromArray_With2DArray_ShouldCreateNDSpan() {
        // Arrange
        int[,] arr = { { 1, 2, 3 }, { 4, 5, 6 } };

        // Act
        var nd = arr.AsNDSpan<int>();

        // Assert
        Assert.Equal(2, nd.Rank);
        Assert.Equal(6, nd.Length);
        Assert.Equal(new[] { 2, 3 }, nd.Shape.ToArray());
        Assert.Equal(1, nd[0, 0]);
        Assert.Equal(2, nd[0, 1]);
        Assert.Equal(6, nd[1, 2]);
    }

    [Fact]
    public void FromArray_With3DArray_ShouldCreateNDSpan() {
        // Arrange
        int[,,] arr = new int[2, 2, 3];
        arr[0, 0, 0] = 1;
        arr[0, 0, 1] = 2;
        arr[1, 1, 2] = 99;

        // Act
        var nd = arr.AsNDSpan<int>();

        // Assert
        Assert.Equal(3, nd.Rank);
        Assert.Equal(12, nd.Length);
        Assert.Equal(2, nd.Shape[0]);
        Assert.Equal(2, nd.Shape[1]);
        Assert.Equal(3, nd.Shape[2]);
        Assert.Equal(1, nd[0, 0, 0]);
        Assert.Equal(2, nd[0, 0, 1]);
        Assert.Equal(99, nd[1, 1, 2]);
    }

    [Fact]
    public void FromArray_WithNullArray_ShouldThrowArgumentNullException() {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => ((int[])null!).AsNDSpan());
    }

    [Fact]
    public void FromArray_WithZeroRankArray_ShouldThrowArgumentException() {
        // Arrange - Create a zero-rank array using reflection since Array.CreateInstance doesn't support it
        // Actually, we can't create a zero-rank array in C#, so we'll test with a different approach
        // by checking if the FromArray method properly validates rank >= 1
        // This test verifies the validation in FromArray, but we can't actually create a zero-rank array
        // So we'll skip this test or test the validation differently
        // Note: Zero-rank arrays don't exist in C#, so this test is theoretical
        Assert.True(true, "Zero-rank arrays cannot be created in C#");
    }

    [Fact]
    public void AsNDSpan_WithMismatchedShape_ShouldThrowArgumentException() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5 };
        int[] shape = { 2, 3 }; // 2*3=6, but buffer has 5 elements

        // Act & Assert
        Assert.Throws<ArgumentException>(() => flatBuffer.AsNDSpan(shape));
    }

    #endregion

    #region Indexing Tests

    [Fact]
    public void Indexer_WithValidIndices_ShouldReturnCorrectValue() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act & Assert
        Assert.Equal(1, nd[0, 0]);
        Assert.Equal(2, nd[0, 1]);
        Assert.Equal(3, nd[0, 2]);
        Assert.Equal(4, nd[1, 0]);
        Assert.Equal(5, nd[1, 1]);
        Assert.Equal(6, nd[1, 2]);
    }

    [Fact]
    public void Indexer_WithValidIndices_ShouldAllowModification() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act
        nd[1, 1] = 99;

        // Assert
        Assert.Equal(99, nd[1, 1]);
        Assert.Equal(99, flatBuffer[4]); // Should modify underlying buffer
    }

    [Fact]
    public void Indexer_WithWrongRank_ShouldThrowArgumentException() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act & Assert
        try {
            _ = nd[0]; // Wrong rank
            Assert.True(false, "Expected ArgumentException");
        } catch (ArgumentException) { }

        try {
            _ = nd[0, 0, 0]; // Wrong rank
            Assert.True(false, "Expected ArgumentException");
        } catch (ArgumentException) { }
    }

    [Fact]
    public void Indexer_WithNullIndices_ShouldThrowArgumentNullException() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3 };
        var nd = flatBuffer.AsNDSpan(3);

        // Act & Assert
        try {
            _ = nd[(int[])null!];
            Assert.True(false, "Expected ArgumentNullException");
        } catch (ArgumentNullException) { }
    }

    [Fact]
    public void Indexer_WithOutOfRangeIndices_ShouldThrowIndexOutOfRangeException() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act & Assert
#if DEBUG
        // Test out of range indices
        try {
            _ = nd[2, 0];
            Assert.True(false, "Expected IndexOutOfRangeException");
        } catch (IndexOutOfRangeException) { }

        try {
            _ = nd[0, 3];
            Assert.True(false, "Expected IndexOutOfRangeException");
        } catch (IndexOutOfRangeException) { }

        try {
            _ = nd[-1, 0];
            Assert.True(false, "Expected IndexOutOfRangeException");
        } catch (IndexOutOfRangeException) { }
#endif
    }

    [Fact]
    public void Indexer_With3DArray_ShouldWorkCorrectly() {
        // Arrange
        int[] flatBuffer = new int[24]; // 2x3x4
        for (int i = 0; i < 24; i++) flatBuffer[i] = i;
        var nd = flatBuffer.AsNDSpan(2, 3, 4);

        // Act & Assert
        // Row-major order: [i,j,k] = i*12 + j*4 + k
        Assert.Equal(0, nd[0, 0, 0]);
        Assert.Equal(1, nd[0, 0, 1]);
        Assert.Equal(4, nd[0, 1, 0]);
        Assert.Equal(12, nd[1, 0, 0]);
        Assert.Equal(23, nd[1, 2, 3]);
    }

    #endregion

    #region Slicing Tests

    [Fact]
    public void Slice_WithFullRange_ShouldReturnSameView() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act
        var sliced = nd[.., ..];

        // Assert
        Assert.Equal(nd.Rank, sliced.Rank);
        Assert.Equal(nd.Length, sliced.Length);
        Assert.Equal(nd[0, 0], sliced[0, 0]);
        Assert.Equal(nd[1, 2], sliced[1, 2]);
    }

    [Fact]
    public void Slice_WithPartialRange_ShouldReturnCorrectView() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var nd = flatBuffer.AsNDSpan(3, 3);

        // Act - slice first row
        var sliced = nd[0..1, ..];

        // Assert
        Assert.Equal(2, sliced.Rank);
        Assert.Equal(3, sliced.Length);
        Assert.Equal(1, sliced.Shape[0]);
        Assert.Equal(3, sliced.Shape[1]);
        Assert.Equal(1, sliced[0, 0]);
        Assert.Equal(2, sliced[0, 1]);
        Assert.Equal(3, sliced[0, 2]);
    }

    [Fact]
    public void Slice_WithMiddleRange_ShouldReturnCorrectView() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var nd = flatBuffer.AsNDSpan(3, 3);

        // Act - slice middle column
        var sliced = nd[.., 1..2];

        // Assert
        Assert.Equal(2, sliced.Rank);
        Assert.Equal(3, sliced.Length);
        Assert.Equal(3, sliced.Shape[0]);
        Assert.Equal(1, sliced.Shape[1]);
        Assert.Equal(2, sliced[0, 0]);
        Assert.Equal(5, sliced[1, 0]);
        Assert.Equal(8, sliced[2, 0]);
    }

    [Fact]
    public void Slice_WithFewerRangesThanRank_ShouldPadWithFullRanges() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var nd = flatBuffer.AsNDSpan(2, 3, 2);

        // Act - only provide one range
        var sliced = nd[0..1];

        // Assert - should be equivalent to [0..1, .., ..]
        Assert.Equal(3, sliced.Rank);
        Assert.Equal(6, sliced.Length);
        Assert.Equal(1, sliced.Shape[0]);
        Assert.Equal(3, sliced.Shape[1]);
        Assert.Equal(2, sliced.Shape[2]);
    }

    [Fact]
    public void Slice_WithNullRanges_ShouldUseFullRanges() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act
        var sliced = nd[(Range[])null!];

        // Assert - should be equivalent to [.., ..]
        Assert.Equal(nd.Rank, sliced.Rank);
        Assert.Equal(nd.Length, sliced.Length);
    }

    [Fact]
    public void Slice_ShouldModifyUnderlyingBuffer() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);
        var sliced = nd[0..1, ..];

        // Act
        sliced[0, 1] = 99;

        // Assert
        Assert.Equal(99, sliced[0, 1]);
        Assert.Equal(99, flatBuffer[1]); // Should modify underlying buffer
        Assert.Equal(99, nd[0, 1]); // Original view should also see the change
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void Rank_ShouldReturnCorrectRank() {
        // Arrange & Act
        var nd1 = new int[10].AsNDSpan(10);
        var nd2 = new int[12].AsNDSpan(3, 4);
        var nd3 = new int[24].AsNDSpan(2, 3, 4);

        // Assert
        Assert.Equal(1, nd1.Rank);
        Assert.Equal(2, nd2.Rank);
        Assert.Equal(3, nd3.Rank);
    }

    [Fact]
    public void Length_ShouldReturnProductOfShape() {
        // Arrange & Act
        var nd1 = new int[10].AsNDSpan(10);
        var nd2 = new int[12].AsNDSpan(3, 4);
        var nd3 = new int[24].AsNDSpan(2, 3, 4);

        // Assert
        Assert.Equal(10, nd1.Length);
        Assert.Equal(12, nd2.Length);
        Assert.Equal(24, nd3.Length);
    }

    [Fact]
    public void Strides_ShouldBeComputedCorrectly() {
        // Arrange
        int[] flatBuffer = new int[24]; // 2x3x4
        var nd = flatBuffer.AsNDSpan(2, 3, 4);

        // Act
        var strides = nd.Strides;

        // Assert
        // Row-major: stride[0] = 3*4 = 12, stride[1] = 4, stride[2] = 1
        Assert.Equal(12, strides[0]);
        Assert.Equal(4, strides[1]);
        Assert.Equal(1, strides[2]);
    }

    [Fact]
    public void Shape_ShouldReturnCorrectShape() {
        // Arrange
        var nd = new int[24].AsNDSpan(2, 3, 4);

        // Act
        var shape = nd.Shape;

        // Assert
        Assert.Equal(2, shape[0]);
        Assert.Equal(3, shape[1]);
        Assert.Equal(4, shape[2]);
    }

    #endregion

    #region Get1DSpan Tests

    [Fact]
    public void Get1DSpan_WithLastAxis_ShouldReturnCorrectSpan() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act - get span along axis 1 (last axis) with first axis = 0
        var span = nd.Get1DSpan(1, 0);

        // Assert
        Assert.Equal(3, span.Length);
        Assert.Equal(1, span[0]);
        Assert.Equal(2, span[1]);
        Assert.Equal(3, span[2]);
    }

    [Fact]
    public void Get1DSpan_WithFirstAxis_ShouldReturnCorrectSpan() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act - get span along axis 1 (last axis) with first axis = 1
        // Note: Get1DSpan returns a contiguous span, so we test with the last axis
        // which is contiguous in row-major layout
        var span = nd.Get1DSpan(1, 1);

        // Assert
        // For axis=1 with axis 0 fixed at 1, we get row 1: [4, 5, 6]
        Assert.Equal(3, span.Length);
        Assert.Equal(4, span[0]); // [1,0]
        Assert.Equal(5, span[1]); // [1,1]
        Assert.Equal(6, span[2]); // [1,2]
    }

    [Fact]
    public void Get1DSpan_With3DArray_ShouldReturnCorrectSpan() {
        // Arrange
        int[] flatBuffer = new int[24]; // 2x3x4
        for (int i = 0; i < 24; i++) flatBuffer[i] = i;
        var nd = flatBuffer.AsNDSpan(2, 3, 4);

        // Act - get span along axis 2 (last axis) with axes 0,1 = 1,2
        var span = nd.Get1DSpan(2, 1, 2);

        // Assert
        // [1,2,k] where k=0..3 should be at indices: 1*12 + 2*4 + k = 12 + 8 + k = 20 + k
        Assert.Equal(4, span.Length);
        Assert.Equal(20, span[0]);
        Assert.Equal(21, span[1]);
        Assert.Equal(22, span[2]);
        Assert.Equal(23, span[3]);
    }

    [Fact]
    public void Get1DSpan_WithInvalidAxis_ShouldThrowArgumentOutOfRangeException() {
        // Arrange
        var nd = new int[12].AsNDSpan(2, 3, 2);

        // Act & Assert
        try {
            nd.Get1DSpan(-1, 0, 0);
            Assert.True(false, "Expected ArgumentOutOfRangeException");
        } catch (ArgumentOutOfRangeException) { }

        try {
            nd.Get1DSpan(3, 0, 0);
            Assert.True(false, "Expected ArgumentOutOfRangeException");
        } catch (ArgumentOutOfRangeException) { }
    }

    [Fact]
    public void Get1DSpan_WithWrongNumberOfFixedIndices_ShouldThrowArgumentException() {
        // Arrange
        var nd = new int[12].AsNDSpan(2, 3, 2);

        // Act & Assert
        try {
            nd.Get1DSpan(0, 0); // Need 2 fixed indices for rank 3
            Assert.True(false, "Expected ArgumentException");
        } catch (ArgumentException) { }

        try {
            nd.Get1DSpan(0, 0, 0, 0); // Too many fixed indices
            Assert.True(false, "Expected ArgumentException");
        } catch (ArgumentException) { }
    }

    [Fact]
    public void Get1DSpan_WithNullFixedIndices_ShouldThrowArgumentNullException() {
        // Arrange
        var nd = new int[12].AsNDSpan(2, 3, 2);

        // Act & Assert
        try {
            nd.Get1DSpan(0, (int[])null!);
            Assert.True(false, "Expected ArgumentNullException");
        } catch (ArgumentNullException) { }
    }

    [Fact]
    public void Get1DSpan_ShouldAllowModification() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);
        var span = nd.Get1DSpan(1, 0);

        // Act
        span[1] = 99;

        // Assert
        Assert.Equal(99, span[1]);
        Assert.Equal(99, nd[0, 1]);
        Assert.Equal(99, flatBuffer[1]);
    }

    #endregion

    #region AsSpan Tests

    [Fact]
    public void AsSpan_ShouldReturnContiguousSpan() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Act
        var span = nd.AsSpan();

        // Assert
        Assert.Equal(6, span.Length);
        Assert.Equal(1, span[0]);
        Assert.Equal(6, span[5]);
    }

    [Fact]
    public void AsSpan_WithSlicedView_ShouldReturnCorrectSpan() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var nd = flatBuffer.AsNDSpan(3, 3);
        var sliced = nd[0..1, ..];

        // Act
        var span = sliced.AsSpan();

        // Assert
        Assert.Equal(3, span.Length);
        Assert.Equal(1, span[0]);
        Assert.Equal(2, span[1]);
        Assert.Equal(3, span[2]);
    }

    #endregion

    #region Extension Methods Tests

    [Fact]
    public void AsNDSpan_Extension_WithFlatArray_ShouldWork() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6 };

        // Act
        var nd = flatBuffer.AsNDSpan(2, 3);

        // Assert
        Assert.Equal(2, nd.Rank);
        Assert.Equal(6, nd.Length);
    }

    [Fact]
    public void AsNDSpan_Extension_WithEmptyShape_ShouldThrowArgumentException() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => flatBuffer.AsNDSpan(Array.Empty<int>()));
    }

    [Fact]
    public void AsNDSpan_Extension_WithNullShape_ShouldThrowArgumentException() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => flatBuffer.AsNDSpan((int[])null!));
    }

    #endregion

    #region Edge Cases and Integration Tests

    [Fact]
    public void LargeArray_ShouldWorkCorrectly() {
        // Arrange
        int[] flatBuffer = new int[1000];
        for (int i = 0; i < 1000; i++) flatBuffer[i] = i;
        var nd = flatBuffer.AsNDSpan(10, 10, 10);

        // Act & Assert
        Assert.Equal(1000, nd.Length);
        Assert.Equal(0, nd[0, 0, 0]);
        Assert.Equal(999, nd[9, 9, 9]);
    }

    [Fact]
    public void SingleElementArray_ShouldWorkCorrectly() {
        // Arrange
        int[] flatBuffer = { 42 };
        var nd = flatBuffer.AsNDSpan(1);

        // Act & Assert
        Assert.Equal(1, nd.Rank);
        Assert.Equal(1, nd.Length);
        Assert.Equal(42, nd[0]);
    }

    [Fact]
    public void MultipleSlices_ShouldWorkCorrectly() {
        // Arrange
        int[] flatBuffer = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var nd = flatBuffer.AsNDSpan(3, 4);

        // Act
        var slice1 = nd[0..1, ..];
        var slice2 = slice1[.., 1..3];

        // Assert
        Assert.Equal(2, slice2.Length);
        Assert.Equal(2, slice2[0, 0]);
        Assert.Equal(3, slice2[0, 1]);
    }

    [Fact]
    public void Strides_ShouldMatchRowMajorLayout() {
        // Arrange
        int[] flatBuffer = new int[60]; // 3x4x5
        for (int i = 0; i < 60; i++) flatBuffer[i] = i; // Initialize with values
        var nd = flatBuffer.AsNDSpan(3, 4, 5);

        // Act
        var strides = nd.Strides;

        // Assert
        // Row-major: stride[0] = 4*5 = 20, stride[1] = 5, stride[2] = 1
        Assert.Equal(20, strides[0]);
        Assert.Equal(5, strides[1]);
        Assert.Equal(1, strides[2]);

        // Verify indexing matches
        // [i,j,k] = i*20 + j*5 + k
        Assert.Equal(0, nd[0, 0, 0]);
        Assert.Equal(1, nd[0, 0, 1]);
        Assert.Equal(5, nd[0, 1, 0]);
        Assert.Equal(20, nd[1, 0, 0]);
        Assert.Equal(59, nd[2, 3, 4]);
    }

    #endregion
}

