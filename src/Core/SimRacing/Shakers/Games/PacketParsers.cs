using System.Buffers.Binary;

namespace Core.SimRacing.Shakers.Games;

internal static class Bytes
{
    public const double G = 9.80665;

    public static float F32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadSingleLittleEndian(b[at..]);

    public static double F64(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadDoubleLittleEndian(b[at..]);

    public static int I32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b[at..]);

    public static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);

    public static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b[at..]);

    public static short I16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadInt16LittleEndian(b[at..]);

    public static double? Finite(double v) => double.IsFinite(v) ? v : null;

    public static double? Positive(double v) => double.IsFinite(v) && v > 0 ? v : null;
}

internal sealed class F1Parser : IPacketParser
{
    public const int HeaderSize = 29, MotionId = 0, TelemetryId = 6, StatusId = 7;
    public const double MotionTimeout = 1;

    private double _vert = double.NaN, _motionAt = double.NegativeInfinity;
    private double? _max, _idle;

    public bool Parse(ReadOnlySpan<byte> p, double now, out ShakerInput input)
    {
        input = default;
        if (p.Length < HeaderSize) return false;
        int format = Bytes.U16(p, 0);
        if (format is < 2023 or > 2026) return false;
        bool v2026 = format >= 2026;
        int cars = v2026 ? 24 : 22, player = p[27], id = p[6];
        if (player >= cars) return false;
        switch (id)
        {
            case MotionId:
            {
                int at = HeaderSize + player * (v2026 ? 54 : 60);
                if (p.Length < at + 48) return false;
                _vert = (v2026 ? Bytes.I16(p, at + 40) / 1000.0 : Bytes.F32(p, at + 44)) * Bytes.G;
                _motionAt = now;
                return false;
            }
            case StatusId:
            {
                int at = HeaderSize + player * (v2026 ? 59 : 55);
                if (p.Length < at + 21) return false;
                _max = Bytes.Positive(Bytes.U16(p, at + 17));
                _idle = Bytes.Positive(Bytes.U16(p, at + 19));
                return false;
            }
            case TelemetryId:
            {
                int at = HeaderSize + player * (v2026 ? 59 : 60);
                if (p.Length < at + 20) return false;
                bool driving = now - _motionAt < MotionTimeout;
                input = new ShakerInput
                {
                    Driving = driving,
                    Throttle = Bytes.Finite(Bytes.F32(p, at + 2)),
                    Gear = (sbyte)p[at + 15],
                    Rpm = Bytes.U16(p, at + 16),
                    VertAccel = driving && double.IsFinite(_vert) ? _vert : null,
                    Time = Bytes.F32(p, 15),
                    IdleRpm = _idle,
                    Redline = _max,
                };
                return true;
            }
        }
        return false;
    }
}

internal sealed class ForzaParser : IPacketParser
{
    public const int Sled = 232, Fm7Dash = 311, HorizonGap = 12;

    public bool Parse(ReadOnlySpan<byte> p, double now, out ShakerInput input)
    {
        input = default;
        if (p.Length < Sled) return false;
        int dash = p.Length switch { 323 or 324 => HorizonGap, >= Fm7Dash => 0, _ => -1 };
        int? gear = null;
        double? throttle = null;
        if (dash >= 0 && p.Length > 307 + dash)
        {
            throttle = p[303 + dash] / 255.0;
            gear = p[307 + dash] switch { 0 => -1, >= 10 => 0, var g => g };
        }
        input = new ShakerInput
        {
            Driving = Bytes.I32(p, 0) == 1,
            Redline = Bytes.Positive(Bytes.F32(p, 8)),
            IdleRpm = Bytes.Positive(Bytes.F32(p, 12)),
            Rpm = Bytes.Finite(Bytes.F32(p, 16)),
            VertAccel = Bytes.Finite(Bytes.F32(p, 24)),
            Time = Bytes.U32(p, 4) / 1000.0,
            Throttle = throttle,
            Gear = gear,
        };
        return true;
    }
}

internal sealed class OutGaugeParser : IPacketParser
{
    public const int GaugeSize = 92, GaugeWithId = 96, BeamMotionSize = 88, LfsSimSize = 64, LfsSimWithId = 68;
    public const uint AbsLight = 1 << 10;
    public const double MotionTimeout = 1;

    private readonly bool _beam;
    private double _vert = double.NaN, _motionAt = double.NegativeInfinity;

    public OutGaugeParser(bool beam) => _beam = beam;

    public bool Parse(ReadOnlySpan<byte> p, double now, out ShakerInput input)
    {
        input = default;
        if (_beam && p.Length == BeamMotionSize && p[0] == 'B' && p[1] == 'N' && p[2] == 'G')
        {
            _vert = Bytes.F32(p, 36);
            _motionAt = now;
            return false;
        }
        if (!_beam && p.Length is LfsSimSize or LfsSimWithId)
        {
            _vert = Bytes.F32(p, 36);
            _motionAt = now;
            return false;
        }
        if (p.Length is not (GaugeSize or GaugeWithId)) return false;
        bool motion = now - _motionAt < MotionTimeout && double.IsFinite(_vert);
        input = new ShakerInput
        {
            Driving = true,
            Gear = p[10] switch { 0 => -1, var g => g - 1 },
            Rpm = Bytes.Finite(Bytes.F32(p, 16)),
            Throttle = Bytes.Finite(Bytes.F32(p, 48)),
            Abs = (Bytes.U32(p, 44) & AbsLight) != 0 && (Bytes.U32(p, 40) & AbsLight) != 0,
            VertAccel = motion ? _vert : null,
            Time = now,
        };
        return true;
    }
}

internal sealed class CodemastersParser : IPacketParser
{
    public const int Size = 264;

    private double _lastVy = double.NaN, _lastTime = double.NaN, _vert;
    private double? _max;

    public bool Parse(ReadOnlySpan<byte> p, double now, out ShakerInput input)
    {
        input = default;
        if (p.Length < Size) return false;
        double time = Bytes.F32(p, 0), vy = Bytes.F32(p, 36);
        if (!double.IsNaN(_lastTime) && time > _lastTime && time - _lastTime < 0.5 && double.IsFinite(vy))
            _vert = (vy - _lastVy) / (time - _lastTime);
        else _vert = 0;
        _lastTime = time;
        _lastVy = vy;
        float gear = Bytes.F32(p, 132);
        _max = Bytes.Positive(Bytes.F32(p, 252) * 10.0) ?? _max;
        input = new ShakerInput
        {
            Driving = true,
            Throttle = Bytes.Finite(Bytes.F32(p, 116)),
            Gear = gear is 10 or < 0 ? -1 : (int)Math.Round(gear),
            Rpm = Bytes.Finite(Bytes.F32(p, 148) * 10.0),
            Redline = _max,
            IdleRpm = Bytes.Positive(Bytes.F32(p, 256) * 10.0),
            VertAccel = _vert,
            Time = time,
        };
        return true;
    }
}

internal sealed class WrcParser : IPacketParser
{
    public const int Size = 237;

    public bool Parse(ReadOnlySpan<byte> p, double now, out ShakerInput input)
    {
        input = default;
        if (p.Length != Size) return false;
        int gear = p[37];
        input = new ShakerInput
        {
            Driving = true,
            Gear = gear == p[39] ? -1 : gear == p[38] ? 0 : gear,
            Redline = Bytes.Positive(Bytes.F32(p, 185)),
            IdleRpm = Bytes.Positive(Bytes.F32(p, 189)),
            Rpm = Bytes.Finite(Bytes.F32(p, 193)),
            Throttle = Bytes.Finite(Bytes.F32(p, 197)),
            VertAccel = Bytes.Finite(Bytes.F32(p, 77)),
            Time = Bytes.F32(p, 8),
        };
        return true;
    }
}
