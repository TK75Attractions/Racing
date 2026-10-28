using System;

/// <summary>Ordered virtual course gates and signed travel; rocking across the finish earns nothing.</summary>
public sealed class LapTraversal
{
    private double last;
    private bool sampled;
    private int nextGate = 1;
    public double Travel { get; private set; }
    public bool CrossedFinish { get; private set; }
    public bool IsArmed => nextGate >= 16 && Travel >= .90;
    public void Reset() { last = 0; sampled = false; nextGate = 1; Travel = 0; CrossedFinish = false; }
    public void Rebase(double progress) { last = Wrap(progress); sampled = true; CrossedFinish = false; }
    public void Observe(double progress, bool continuous)
    {
        if (double.IsNaN(progress) || double.IsInfinity(progress)) return;
        progress = Wrap(progress);
        if (!sampled) { Rebase(progress); return; }
        double delta = progress - last;
        if (delta > .5) delta -= 1;
        if (delta < -.5) delta += 1;
        if (continuous && Math.Abs(delta) < .15)
        {
            Travel = Math.Max(0, Travel + delta);
            if (delta > 0 && progress < last && IsArmed) CrossedFinish = true;
            // Never use the finish wrap itself to fill missing gates.
            if (delta > 0 && progress >= last)
                while (nextGate < 16 && last <= nextGate / 16.0 && progress >= nextGate / 16.0) nextGate++;
        }
        last = progress;
    }
    private static double Wrap(double value) => value - Math.Floor(value);
}
