using System.Diagnostics;
using System.Windows.Media;

namespace PitBox.Views;

internal interface IFrameStep
{
    bool Step(double nowMs);
}

internal static class FrameDriver
{
    private static readonly List<IFrameStep> Active = new();
    private static readonly List<IFrameStep> Running = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static bool _hooked;

    public static double Now => Clock.Elapsed.TotalMilliseconds;

    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    public static double Spring(double t)
    {
        t = Math.Clamp(t, 0, 1);
        const double c1 = 1.2, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    public static void Run(IFrameStep step)
    {
        if (!Active.Contains(step)) Active.Add(step);
        if (_hooked) return;
        _hooked = true;
        CompositionTarget.Rendering += OnFrame;
    }

    public static void Stop(IFrameStep step) => Active.Remove(step);

    private static void OnFrame(object? sender, EventArgs e)
    {
        double now = Now;
        Running.Clear();
        Running.AddRange(Active);
        foreach (var step in Running)
            if (!step.Step(now)) Active.Remove(step);
        Running.Clear();
        if (Active.Count > 0) return;
        CompositionTarget.Rendering -= OnFrame;
        _hooked = false;
    }
}
