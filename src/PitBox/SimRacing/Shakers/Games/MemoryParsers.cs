namespace PitBox.SimRacing.Shakers.Games;

internal enum KunosGame { Classic, Competizione, Evo, Rally }

internal sealed class KunosParser : IMemoryParser
{
    public const int PhysicsSize = 800, LiveStatus = 2, EvoGraphicsSize = 48, StaticMaxRpm = 412;

    private readonly KunosGame _game;
    private readonly byte[] _physics = new byte[PhysicsSize], _graphics = new byte[EvoGraphicsSize], _static = new byte[4];
    private int _packet = int.MinValue;

    public KunosParser(KunosGame game)
    {
        _game = game;
        Maps = game == KunosGame.Evo
            ? new[] { @"Local\acevo_pmf_physics|Local\acpmf_physics", @"Local\acevo_pmf_graphics|Local\acpmf_graphics" }
            : new[] { @"Local\acpmf_physics", @"Local\acpmf_graphics", @"Local\acpmf_static" };
    }

    public string[] Maps { get; }

    public bool Parse(IMemoryView view, double now, out ShakerInput input) =>
        Parse(view.Read(0, 0, _physics, PhysicsSize) ? _physics : null,
            view.Read(1, 0, _graphics, EvoGraphicsSize) ? _graphics : null,
            _game != KunosGame.Evo && view.Read(2, StaticMaxRpm, _static, 4) ? Bytes.I32(_static, 0) : null,
            now, out input);

    internal bool Parse(byte[]? physics, byte[]? graphics, int? staticMaxRpm, double now, out ShakerInput input)
    {
        input = default;
        if (physics is null) return false;
        int packet = Bytes.I32(physics, 0);
        if (packet == _packet) return false;
        _packet = packet;
        bool live = _game == KunosGame.Rally || graphics is not null && Bytes.I32(graphics, 4) == LiveStatus;
        int gear = Bytes.I32(physics, 16);
        double? max = Bytes.Positive(Bytes.I32(physics, 588));
        if (max is null && staticMaxRpm is { } s) max = Bytes.Positive(s);
        bool evo = _game == KunosGame.Evo && graphics is not null;
        input = new ShakerInput
        {
            Driving = live,
            Throttle = Bytes.Finite(Bytes.F32(physics, 4)),
            Gear = gear - 1,
            Rpm = Bytes.I32(physics, 20),
            Redline = max,
            VertAccel = Bytes.Finite(Bytes.F32(physics, 48) * Bytes.G),
            Abs = evo ? graphics![46] != 0 : _game == KunosGame.Competizione && Bytes.I32(physics, 676) != 0,
            Limiter = evo && graphics![42] != 0,
            Time = now,
        };
        return true;
    }
}

internal sealed class RFactor2Parser : IMemoryParser
{
    public const int VehicleSize = 1888, ScoringVehicles = 560, ScoringStride = 584, MaxVehicles = 128;

    private readonly byte[] _head = new byte[16], _veh = new byte[VehicleSize], _scoring = new byte[8], _scoredCar = new byte[200], _end = new byte[8];
    private double _elapsed = double.NaN;
    private int _playerId = -1, _slot = -1;
    private double _resolvedAt = double.NegativeInfinity;

    public string[] Maps { get; } = { "$rFactor2SMMP_Telemetry$", "$rFactor2SMMP_Scoring$" };

    public bool Parse(IMemoryView view, double now, out ShakerInput input)
    {
        input = default;
        if (now - _resolvedAt > 0.2) Resolve(view, now);
        if (_playerId < 0 || !view.Read(0, 0, _head, 16)) return false;
        int count = Math.Clamp(Bytes.I32(_head, 12), 0, MaxVehicles);
        if (_slot < 0 || _slot >= count || !view.Read(0, 16 + (long)_slot * VehicleSize, _veh, VehicleSize) || Bytes.I32(_veh, 0) != _playerId)
        {
            _slot = -1;
            for (int i = 0; i < count && _slot < 0; i++)
                if (view.Read(0, 16 + (long)i * VehicleSize, _veh, 4) && Bytes.I32(_veh, 0) == _playerId) _slot = i;
            if (_slot < 0 || !view.Read(0, 16 + (long)_slot * VehicleSize, _veh, VehicleSize)) return false;
        }
        if (!view.Read(0, 0, _end, 8) || Bytes.U32(_head, 0) != Bytes.U32(_end, 4)) return false;
        return Vehicle(_veh, _inRealtime, ref _elapsed, now, out input);
    }

    private bool _inRealtime;

    private void Resolve(IMemoryView view, double now)
    {
        _resolvedAt = now;
        _playerId = -1;
        if (!view.Read(1, 116, _scoring, 8)) return;
        int count = Math.Clamp(Bytes.I32(_scoring, 0), 0, MaxVehicles);
        _inRealtime = _scoring[127 - 116] != 0;
        for (int i = 0; i < count; i++)
        {
            if (!view.Read(1, ScoringVehicles + (long)i * ScoringStride, _scoredCar, 198)) return;
            if (_scoredCar[196] != 0)
            {
                _playerId = _scoredCar[197] == 0 ? Bytes.I32(_scoredCar, 0) : -1;
                return;
            }
        }
    }

    internal static bool Vehicle(byte[] v, bool inRealtime, ref double lastElapsed, double now, out ShakerInput input)
    {
        input = default;
        double elapsed = Bytes.F64(v, 12);
        if (elapsed == lastElapsed) return false;
        lastElapsed = elapsed;
        input = new ShakerInput
        {
            Driving = inRealtime,
            VertAccel = Bytes.Finite(Bytes.F64(v, 216)),
            Gear = Bytes.I32(v, 352),
            Rpm = Bytes.Finite(Bytes.F64(v, 356)),
            Throttle = Bytes.Finite(Bytes.F64(v, 420)),
            Redline = Bytes.Positive(Bytes.F64(v, 532)),
            Time = elapsed,
        };
        return true;
    }
}

internal sealed class LmuParser : IMemoryParser
{
    public const int Telemetry = 128464, Vehicles = 128468, InRealtime = 1747, AbsActive = 746;

    private readonly byte[] _head = new byte[3], _flag = new byte[1], _veh = new byte[RFactor2Parser.VehicleSize];
    private double _elapsed = double.NaN;

    public string[] Maps { get; } = { "LMU_Data" };

    public bool Parse(IMemoryView view, double now, out ShakerInput input)
    {
        input = default;
        if (!view.Read(0, Telemetry, _head, 3) || _head[2] == 0 || _head[1] >= 104) return false;
        if (!view.Read(0, Vehicles + (long)_head[1] * RFactor2Parser.VehicleSize, _veh, _veh.Length)) return false;
        bool realtime = view.Read(0, InRealtime, _flag, 1) && _flag[0] != 0;
        if (!RFactor2Parser.Vehicle(_veh, realtime, ref _elapsed, now, out input)) return false;
        input.Abs = _veh[AbsActive] != 0;
        return true;
    }
}

internal sealed class PcarsParser : IMemoryParser
{
    public const int Size = 7324, Playing = 2, AbsFlag = 16;

    private readonly byte[] _b = new byte[Size];
    private uint _seq = uint.MaxValue;

    public string[] Maps { get; } = { "$pcars2$" };

    public bool Parse(IMemoryView view, double now, out ShakerInput input)
    {
        input = default;
        return view.Read(0, 0, _b, Size) && Parse(_b, now, out input);
    }

    internal bool Parse(byte[] b, double now, out ShakerInput input)
    {
        input = default;
        uint seq = Bytes.U32(b, 7320);
        if ((seq & 1) != 0 || seq == _seq) return false;
        _seq = seq;
        input = new ShakerInput
        {
            Driving = Bytes.U32(b, 8) == Playing,
            Rpm = Bytes.Finite(Bytes.F32(b, 6852)),
            Redline = Bytes.Positive(Bytes.F32(b, 6856)),
            Throttle = Bytes.Finite(Bytes.F32(b, 6864)),
            Gear = Bytes.I32(b, 6876),
            Abs = b[6888] != 0 || (Bytes.U32(b, 6816) & AbsFlag) != 0,
            VertAccel = Bytes.Finite(Bytes.F32(b, 6960)),
            Time = now,
        };
        return true;
    }
}

internal sealed class RaceRoomParser : IMemoryParser
{
    public const int Size = 1576, AbsActive = 5;
    public const double RpsToRpm = 60 / (2 * Math.PI), TickSeconds = 1 / 400.0;

    private readonly byte[] _b = new byte[Size];
    private int _ticks = int.MinValue;

    public string[] Maps { get; } = { "$R3E" };

    public bool Parse(IMemoryView view, double now, out ShakerInput input)
    {
        input = default;
        return view.Read(0, 0, _b, Size) && Parse(_b, out input);
    }

    internal bool Parse(byte[] b, out ShakerInput input)
    {
        input = default;
        if (Bytes.I32(b, 0) != 3) return false;
        int ticks = Bytes.I32(b, 44);
        if (ticks == _ticks) return false;
        _ticks = ticks;
        bool driving = Bytes.I32(b, 20) == 0 && Bytes.I32(b, 24) == 0 && Bytes.I32(b, 28) == 0 && Bytes.I32(b, 36) == 0 && Bytes.I32(b, 1388) == 0;
        int gear = Bytes.I32(b, 1408);
        float throttle = Bytes.F32(b, 1500);
        input = new ShakerInput
        {
            Driving = driving,
            VertAccel = Bytes.Finite(Bytes.F64(b, 160)),
            Rpm = Bytes.Finite(Bytes.F32(b, 1396) * RpsToRpm),
            Redline = Bytes.Positive(Bytes.F32(b, 1400) * RpsToRpm),
            Gear = gear < -1 ? null : gear,
            Throttle = throttle < 0 ? null : throttle,
            Abs = Bytes.I32(b, 1536) == AbsActive,
            Time = ticks * TickSeconds,
        };
        return true;
    }
}
