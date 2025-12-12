using System.Text.Json.Serialization;

namespace RadarMoves.Shared;

public class UserState {
    [JsonPropertyName("UserId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("UserName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("MouseX")]
    public int MouseX { get; set; } = 0;

    [JsonPropertyName("MouseY")]
    public int MouseY { get; set; } = 0;
}

