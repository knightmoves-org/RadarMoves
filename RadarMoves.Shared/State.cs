using System.Text.Json.Serialization;

namespace RadarMoves.Shared;

public class State {
    [JsonPropertyName("UserState")]
    public UserState? UserState { get; set; }
}

