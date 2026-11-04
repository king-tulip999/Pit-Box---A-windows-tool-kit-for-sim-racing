using System.Text.Json.Serialization;

namespace PitBox.Config;

public enum ShakerChannel { Both, Left, Right }

public enum ShakerMode { Effects, GameAudio, Off }

public sealed class ShakerEffectSettings
{
    public int Strength { get; set; } = 60;

    public void Clamp() => Strength = Math.Clamp(Strength, 0, 100);
}

public sealed class ShakerSettings
{
    public const string IracingId = "iracing";

    public const int MinHz = 15, MaxHz = 120, MinCutoffHz = 40, MinPort = 1024, MaxPort = 65535;

    public static readonly string[] DefaultGames =
    {
        "BeamNG.drive.x64.exe", "AssettoCorsaEVO.exe", "acs.exe", "AC2-Win64-Shipping.exe", "rFactor2.exe", "Le Mans Ultimate.exe",
        "AMS2AVX.exe", "RRRE64.exe", "WRC.exe", "dirtrally2.exe", "eurotrucks2.exe", "amtrucks.exe",
    };

    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public ShakerChannel Channel { get; set; } = ShakerChannel.Both;
    public int Volume { get; set; } = 70;

    [JsonPropertyName("InIracing")]
    public ShakerMode Mode { get; set; } = ShakerMode.Effects;
    public List<string> GamesOff { get; set; } = new();
    public Dictionary<string, int> Ports { get; set; } = new();
    public bool OtherGames { get; set; }
    public int GameAudioStrength { get; set; } = 50;
    public int GameAudioCutoffHz { get; set; } = 80;
    public List<string> Games { get; set; } = DefaultGames.ToList();

    public ShakerEffectSettings Engine { get; set; } = new() { Strength = 35 };
    public int EngineLowHz { get; set; } = 25;
    public int EngineHighHz { get; set; } = 60;
    public ShakerEffectSettings Shifts { get; set; } = new() { Strength = 80 };
    public ShakerEffectSettings Abs { get; set; } = new() { Strength = 70 };
    public ShakerEffectSettings Road { get; set; } = new() { Strength = 60 };
    public ShakerEffectSettings Limiter { get; set; } = new() { Strength = 50 };

    public void Normalize()
    {
        Volume = Math.Clamp(Volume, 0, 100);
        foreach (var e in AllEffects()) e.Clamp();
        EngineLowHz = Math.Clamp(EngineLowHz, MinHz, MaxHz);
        EngineHighHz = Math.Clamp(EngineHighHz, EngineLowHz, MaxHz);
        GameAudioStrength = Math.Clamp(GameAudioStrength, 0, 100);
        GameAudioCutoffHz = Math.Clamp(GameAudioCutoffHz, MinCutoffHz, MaxHz);
        Games = ParseGames(string.Join(",", Games ?? DefaultGames.ToList()));
        GamesOff = (GamesOff ?? new()).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Ports = (Ports ?? new()).Where(p => p.Key.Length > 0 && p.Value is >= MinPort and <= MaxPort).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    }

    [JsonIgnore]
    public bool IracingOn => GameOn(IracingId);

    public bool GameOn(string id) => !GamesOff.Contains(id, StringComparer.OrdinalIgnoreCase);

    public void SetGameOn(string id, bool on)
    {
        GamesOff.RemoveAll(g => string.Equals(g, id, StringComparison.OrdinalIgnoreCase));
        if (!on) GamesOff.Add(id);
    }

    public int Port(string id, int fallback) => Ports.TryGetValue(id, out int port) ? port : fallback;

    public static List<string> ParseGames(string text) =>
        text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IEnumerable<ShakerEffectSettings> AllEffects() => new[] { Engine ??= new(), Shifts ??= new(), Abs ??= new(), Road ??= new(), Limiter ??= new() };
}
