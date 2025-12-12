using RadarMoves.Server.Data;

namespace RadarMoves.Server.Services;

/// <summary>
/// Service to manage and provide access to the radar dataset
/// Uses IRadarDataProvider interface for data access
/// </summary>
public class RadarDatasetService(IRadarDataProvider dataProvider, ILogger<RadarDatasetService> logger) {
    private readonly IRadarDataProvider _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
    private readonly ILogger<RadarDatasetService> _logger = logger;
    public IRadarDataProvider DataProvider => _dataProvider;
    public ILogger<RadarDatasetService> Logger => _logger;
}

