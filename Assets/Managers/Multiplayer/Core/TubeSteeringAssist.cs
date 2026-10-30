using System;

/// <summary>Gentle tube-axis heading support; the driver's handle remains effective.</summary>
public static class TubeSteeringAssist
{
    public static float Blend(float driverAngle, float headingError, float yawRateDegrees, float limit)
    {
        if (!Finite(driverAngle) || !Finite(headingError) || !Finite(yawRateDegrees) || !Finite(limit) || limit<=0f) return driverAngle;
        float target=Clamp(headingError*1.4f-yawRateDegrees*.10f,-limit,limit);
        float handle=Math.Min(1f,Math.Abs(driverAngle)/limit);
        float correction=(target-driverAngle)*(.55f-.40f*handle);
        return Clamp(driverAngle+Clamp(correction,-12f,12f),-limit,limit);
    }
    private static float Clamp(float value,float min,float max)=>Math.Max(min,Math.Min(max,value));
    private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
}
