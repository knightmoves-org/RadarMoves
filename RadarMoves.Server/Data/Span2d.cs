namespace RadarMoves.Server.Data;



public readonly ref struct Span2D(Span<float> data, int nRays, int nBins) {
    private readonly Span<float> _data = data;
    private readonly int _nRays = nRays;
    private readonly int _nBins = nBins;

    public int NRays => _nRays;
    public int NBins => _nBins;
    private static Span<float> ToSpan(float[,] data) {
        int nRays = data.GetLength(0);
        int nBins = data.GetLength(1);
        var buffer = new float[nRays * nBins];
        for (int i = 0; i < nRays; i++) {
            for (int j = 0; j < nBins; j++) {
                buffer[i * nBins + j] = data[i, j];
            }
        }
        return buffer.AsSpan();
    }
    public Span2D(float[,] data) : this(ToSpan(data), data.GetLength(0), data.GetLength(1)) { }

    public ref float this[int ray, int bin] => ref _data[ray * _nBins + bin];
    public float[,] ToArray() {
        var array = new float[_nRays, _nBins];
        for (int i = 0; i < _nRays; i++) {
            for (int j = 0; j < _nBins; j++) {
                array[i, j] = this[i, j];
            }
        }
        return array;
    }
}