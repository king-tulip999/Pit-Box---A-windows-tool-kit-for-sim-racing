using Core.Config;

namespace Core.SimRacing.Shakers;

internal sealed class GameAudioBridge
{
    public const double TargetSeconds = 0.04, MaxSeconds = 0.15, StrengthToGain = 1 / 25.0;

    private readonly object _gate = new();
    private float[] _ring = Array.Empty<float>();
    private long _written;
    private double _read;
    private bool _primed;
    private int _inRate, _cutoff = -1;
    private Biquad _low1, _low2, _high;

    public void Write(ReadOnlySpan<float> mono, int rate, int cutoffHz)
    {
        if (rate <= 0) return;
        lock (_gate)
        {
            if (rate != _inRate)
            {
                _inRate = rate;
                _ring = new float[rate];
                _written = 0;
                _read = 0;
                _primed = false;
                _cutoff = -1;
            }
            if (cutoffHz != _cutoff)
            {
                _cutoff = cutoffHz;
                _low1 = Biquad.LowPass(rate, cutoffHz, 0.5412);
                _low2 = Biquad.LowPass(rate, cutoffHz, 1.3066);
                _high = Biquad.HighPass(rate, ShakerSettings.MinHz, Math.Sqrt(0.5));
            }
            int length = _ring.Length;
            foreach (float x in mono)
            {
                double y = _high.Process(_low2.Process(_low1.Process(x)));
                _ring[_written % length] = (float)y;
                _written++;
            }
        }
    }

    public void Render(Span<float> output, int outRate, ShakerSettings s)
    {
        double gain = s.GameAudioStrength * StrengthToGain, volume = s.Volume / 100.0;
        lock (_gate)
        {
            if (_inRate == 0 || outRate <= 0)
            {
                output.Clear();
                return;
            }
            double step = (double)_inRate / outRate;
            double target = _inRate * TargetSeconds;
            if (_written - _read > _inRate * MaxSeconds) _read = _written - target;
            if (!_primed && _written - _read >= target) _primed = true;
            int length = _ring.Length;
            for (int i = 0; i < output.Length; i++)
            {
                if (!_primed || _read + 1 >= _written)
                {
                    _primed = false;
                    output[i] = 0;
                    continue;
                }
                long k = (long)_read;
                double f = _read - k;
                double x = _ring[k % length] * (1 - f) + _ring[(k + 1) % length] * f;
                output[i] = (float)(Math.Tanh(x * gain) * volume);
                _read += step;
            }
        }
    }

    private struct Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

        public static Biquad LowPass(int rate, double hz, double q) => Make(rate, hz, q, low: true);

        public static Biquad HighPass(int rate, double hz, double q) => Make(rate, hz, q, low: false);

        private static Biquad Make(int rate, double hz, double q, bool low)
        {
            double w = Math.Tau * Math.Min(hz, rate * 0.45) / rate;
            double cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            double side = low ? (1 - cos) / 2 : (1 + cos) / 2;
            return new Biquad
            {
                _b0 = side / a0,
                _b1 = (low ? 1 - cos : -(1 + cos)) / a0,
                _b2 = side / a0,
                _a1 = -2 * cos / a0,
                _a2 = (1 - alpha) / a0,
            };
        }

        public double Process(double x)
        {
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }
    }
}
