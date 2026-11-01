using PitBox.Config;

namespace PitBox.SimRacing.Shakers;

internal enum ShakerEffect { Engine, Shifts, Abs, Road, Limiter }

internal sealed class ShakerMixer
{
    public const double ShiftSeconds = 0.12, TestSeconds = 1.5, AbsPulseHz = 12, LimiterPulseHz = 25, SmoothingSeconds = 0.015;

    private readonly int _rate;
    private readonly double _smooth;
    private double _enginePhase, _absPhase, _limiterPhase, _shiftPhase, _gatePhase, _limiterGate;
    private double _engineFreq = double.NaN, _engineAmp, _road, _roadNoise;
    private double _shiftLeft;
    private int _shiftsSeen = int.MinValue;
    private uint _seed = 0x9E3779B9;
    private volatile int _test = -1;
    private double _testLeft, _testAt;

    public ShakerMixer(int sampleRate)
    {
        _rate = Math.Max(8000, sampleRate);
        _smooth = 1 - Math.Exp(-1.0 / (_rate * SmoothingSeconds));
    }

    public bool Testing => _test >= 0 || _testLeft > 0;

    public void Test(ShakerEffect effect) => _test = (int)effect;

    public void Render(Span<float> mono, ShakerLevels live, ShakerSettings s)
    {
        int asked = _test;
        if (asked >= 0)
        {
            _test = -1;
            _testLeft = TestSeconds;
            _testAt = 0;
            _shiftsSeen = int.MinValue;
            _testEffect = (ShakerEffect)asked;
        }
        double dt = 1.0 / _rate;
        double volume = s.Volume / 100.0;
        bool testing = _testLeft > 0;
        for (int i = 0; i < mono.Length; i++)
        {
            var levels = testing ? TestLevels() : live;
            bool Use(ShakerEffect e, ShakerEffectSettings x) => testing ? _testEffect == e : x.Strength > 0;
            double sum = 0;
            if (levels.Active && Use(ShakerEffect.Engine, s.Engine))
            {
                double freq = s.EngineLowHz + levels.EngineFraction * (s.EngineHighHz - s.EngineLowHz);
                if (double.IsNaN(_engineFreq)) _engineFreq = freq;
                _engineFreq += (freq - _engineFreq) * _smooth;
                _engineAmp += ((0.3 + 0.7 * levels.EngineLoad) - _engineAmp) * _smooth;
                _enginePhase = Wrap(_enginePhase + _engineFreq * dt);
                sum += Math.Sin(_enginePhase * Math.Tau) * _engineAmp * s.Engine.Strength / 100.0;
            }
            else _engineAmp += (0 - _engineAmp) * _smooth;
            if (levels.Shifts != _shiftsSeen)
            {
                if (_shiftsSeen != int.MinValue && Use(ShakerEffect.Shifts, s.Shifts)) { _shiftLeft = ShiftSeconds; _shiftPhase = 0; }
                _shiftsSeen = levels.Shifts;
            }
            if (_shiftLeft > 0)
            {
                double env = _shiftLeft / ShiftSeconds;
                _shiftPhase = Wrap(_shiftPhase + s.EngineLowHz * dt);
                sum += Math.Sin(_shiftPhase * Math.Tau) * env * env * s.Shifts.Strength / 100.0;
                _shiftLeft -= dt;
            }
            _gatePhase = Wrap(_gatePhase + AbsPulseHz * dt);
            if (levels.Active && levels.Abs && Use(ShakerEffect.Abs, s.Abs) && _gatePhase < 0.5)
            {
                _absPhase = Wrap(_absPhase + (s.EngineLowHz + s.EngineHighHz) / 2.0 * dt);
                sum += Math.Sin(_absPhase * Math.Tau) * s.Abs.Strength / 100.0;
            }
            double road = levels.Active && Use(ShakerEffect.Road, s.Road) ? levels.Road : 0;
            _road += (road - _road) * _smooth;
            _roadNoise += (Noise() - _roadNoise) * (1 - Math.Exp(-Math.Tau * s.EngineHighHz * dt));
            sum += _roadNoise * 2 * _road * s.Road.Strength / 100.0;
            _limiterGate = Wrap(_limiterGate + LimiterPulseHz * dt);
            if (levels.Active && levels.Limiter && Use(ShakerEffect.Limiter, s.Limiter) && _limiterGate < 0.5)
            {
                _limiterPhase = Wrap(_limiterPhase + s.EngineHighHz * dt);
                sum += Math.Sin(_limiterPhase * Math.Tau) * s.Limiter.Strength / 100.0;
            }
            mono[i] = (float)(Math.Tanh(sum) * volume);
            if (testing)
            {
                _testAt += dt;
                _testLeft -= dt;
                if (_testLeft <= 0)
                {
                    testing = false;
                    _shiftsSeen = int.MinValue;
                }
            }
        }
    }

    private ShakerEffect _testEffect;

    private ShakerLevels TestLevels()
    {
        double t = _testAt / TestSeconds;
        var l = new ShakerLevels { Active = true };
        switch (_testEffect)
        {
            case ShakerEffect.Engine: l.EngineFraction = t; l.EngineLoad = 1; break;
            case ShakerEffect.Shifts: l.Shifts = (int)(t * 4); break;
            case ShakerEffect.Abs: l.Abs = true; break;
            case ShakerEffect.Road: l.Road = 0.8; break;
            case ShakerEffect.Limiter: l.Limiter = true; break;
        }
        return l;
    }

    private double Noise()
    {
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return _seed / (double)uint.MaxValue * 2 - 1;
    }

    private static double Wrap(double phase) => phase >= 1 ? phase - Math.Floor(phase) : phase;
}
