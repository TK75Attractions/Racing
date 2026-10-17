using System;

/// <summary>Six-speed automatic for engine/HUD telemetry, independent of arcade driving forces.</summary>
public sealed class AutomaticGearbox
{
    public const float IdleRpm = 950f;
    public const float RedlineRpm = 7800f;
    // Long racing gears: full-load shifts at roughly 61, 88, 121, 161 and 210 km/h.
    private static readonly float[] Ratios = { 3.60f, 2.48f, 1.80f, 1.35f, 1.04f, .82f };
    private const float FinalDrive = 4.1f;
    private const float WheelCircumference = 1.96f;
    private const float ShiftDuration = .22f;
    private float shiftRemaining, cooldown;
    private bool wasReverse;
    public int Gear { get; private set; } = 1;
    public float Rpm { get; private set; } = IdleRpm;
    public float Load { get; private set; }
    public bool IsShifting => shiftRemaining > 0f;
    public int ShiftSerial { get; private set; }
    public bool Downshift { get; private set; }
    public float Combustion => IsShifting && !Downshift ? .16f : 1f;

    public void Reset()
    {
        Gear = 1; Rpm = IdleRpm; Load = 0f;
        shiftRemaining = cooldown = 0f; wasReverse = false;
        Downshift = false;
    }

    public void Step(float dt, float forwardSpeed, float throttle, bool grounded, float boost)
    {
        if (dt <= 0f) return;
        dt = Math.Min(dt, .1f);
        throttle = Clamp(throttle, 0f, 1f);
        boost = Clamp(boost, 0f, 1f);
        bool reverse = forwardSpeed < -.8f;
        float speed = Math.Abs(forwardSpeed);
        if (speed < .8f || reverse != wasReverse)
        {
            Gear = 1; shiftRemaining = 0f; cooldown = .25f;
        }
        wasReverse = reverse;
        shiftRemaining = Math.Max(0f, shiftRemaining - dt);
        cooldown = Math.Max(0f, cooldown - dt);
        float wheelRpm = speed * 60f / WheelCircumference * FinalDrive;
        float coupled = wheelRpm * Ratios[Gear - 1];
        // Stay near redline even at part throttle; lifting for a corner must not
        // select a taller gear merely because the old economy threshold fell.
        float upshift = 7500f + throttle * 100f;
        bool allowUpshift = throttle > .1f || coupled > RedlineRpm + 150f;
        if (grounded && !reverse && !IsShifting && cooldown <= 0f)
        {
            if (allowUpshift && coupled > upshift && Gear < Ratios.Length) Shift(1);
            else if (Gear > 1 && (coupled < 3600f ||
                (throttle > .55f && coupled < 4800f && wheelRpm * Ratios[Gear - 2] < 7000f))) Shift(-1);
            coupled = wheelRpm * Ratios[Gear - 1];
        }
        // Torque-converter slip on launch; free-rev only when airborne.
        float target = grounded ? Math.Max(coupled, IdleRpm + throttle * 2100f) :
            IdleRpm + throttle * 6600f;
        if (IsShifting && Downshift) target += 420f;
        target += boost * throttle * 220f;
        target = Clamp(target, IdleRpm, RedlineRpm);
        Rpm += (target - Rpm) * (1f - (float)Math.Exp(-dt * (IsShifting ? 22f : 10f)));
        float targetLoad = throttle * (.7f + .3f * boost);
        Load += (targetLoad - Load) * (1f - (float)Math.Exp(-dt * 12f));
    }

    private void Shift(int direction)
    {
        Gear += direction; Downshift = direction < 0; ShiftSerial++;
        shiftRemaining = ShiftDuration; cooldown = .65f;
    }
    private static float Clamp(float x, float min, float max) => Math.Max(min, Math.Min(max, x));
}
