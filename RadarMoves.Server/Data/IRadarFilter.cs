namespace RadarMoves.Server.Data;

public interface IRadarFilter<T> where T : unmanaged {
    abstract void Apply(Span2D<T> grid);
    public void Invoke(Span2D<T> grid) => Apply(grid);
    public void Invoke(T[,] grid) => Apply(grid.AsSpan());

}

