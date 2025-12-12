namespace RadarMoves.Shared.Services;

public class RadarControlsService {
    public List<string> Timestamps { get; set; } = [];
    public List<float> ElevationAngles { get; set; } = [];
    public List<string> AvailableChannels { get; set; } = ["Reflectivity", "RadialVelocity", "SpectralWidth", "TotalPower"];
    public List<string> AvailableViewTypes { get; set; } = ["Radar", "Raw Data", "Filtered Data"];

    public string SelectedChannel { get; set; } = "Reflectivity";
    public string SelectedViewType { get; set; } = "Radar";
    public int TimeIndex { get; set; } = 0;
    public int ElevationIndex { get; set; } = 0;
    public float Latitude { get; set; } = 38.9f;
    public float Longitude { get; set; } = -92.3f;

    public DateTime SelectedTime {
        get {
            if (TimeIndex >= 0 && TimeIndex < Timestamps.Count && DateTime.TryParse(Timestamps[TimeIndex], out var dt))
                return dt;
            return DateTime.Now;
        }
    }

    public float SelectedElevation {
        get {
            if (ElevationIndex >= 0 && ElevationIndex < ElevationAngles.Count)
                return ElevationAngles[ElevationIndex];
            return 0f;
        }
    }

    public (DateTime Start, DateTime End)? TimeRange { get; set; }

    public event Action? OnStateChanged;

    public void NotifyStateChanged() {
        OnStateChanged?.Invoke();
    }
}

