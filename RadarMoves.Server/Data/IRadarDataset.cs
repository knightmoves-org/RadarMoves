namespace RadarMoves.Server.Data;

public interface IRadarDataset<T> where T : unmanaged {
    abstract public T[,] this[int idx] { get; }
}

