using System;

/// <summary>Compares elapsed time at the same verified course position on consecutive laps.</summary>
public sealed class LapComparison
{
    private const int Bins=256;
    private float[] current=new float[Bins+1],previous=new float[Bins+1];
    private int lap=-1,lastBin;
    private float lastFraction,lastSeconds,lastCompleted;
    public LapComparison(){Reset();}
    public void Reset(){Clear(current);Clear(previous);lap=-1;lastBin=0;lastFraction=lastSeconds=lastCompleted=0;}
    private static void Clear(float[] samples){for(int i=0;i<samples.Length;i++)samples[i]=float.NaN;}
    public float? Sample(int number,float fraction,float elapsed,float completed)
    {
        if(float.IsNaN(fraction)||float.IsNaN(elapsed)||fraction<0||fraction>1||elapsed<0)return null;
        if(number<lap||number==lap&&elapsed+.01f<lastSeconds)Reset();
        if(number!=lap)
        {
            if(number==lap+1&&lap>=0)
            {
                Record(1,Math.Max(lastSeconds,completed-lastCompleted));var swap=previous;previous=current;current=swap;Clear(current);
            }
            else {Clear(current);Clear(previous);}
            lap=number;lastBin=0;lastFraction=lastSeconds=0;lastCompleted=completed;current[0]=0;
        }
        Record(fraction,elapsed);
        float position=fraction*Bins;int left=Math.Min(Bins,(int)position),right=Math.Min(Bins,left+1);
        if(float.IsNaN(previous[left])||float.IsNaN(previous[right]))return null;
        return elapsed-(previous[left]+(previous[right]-previous[left])*(position-left));
    }
    private void Record(float fraction,float elapsed)
    {
        if(fraction<lastFraction)return;
        int end=Math.Min(Bins,(int)(fraction*Bins));
        for(int i=lastBin+1;i<=end;i++)
        {
            float t=fraction>lastFraction?(i/(float)Bins-lastFraction)/(fraction-lastFraction):0;
            current[i]=lastSeconds+(elapsed-lastSeconds)*t;
        }
        lastBin=end;lastFraction=fraction;lastSeconds=elapsed;
    }
}
