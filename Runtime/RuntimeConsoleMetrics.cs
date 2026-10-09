using System;

/// <summary>Bounded rolling frame history; independent of Unity for testing.</summary>
public sealed class RuntimeConsoleMetrics
{
    private readonly float[] samples;
    private int next;
    private double total;
    public int Count { get; private set; }
    public float AverageMilliseconds { get { return Count == 0 ? 0 : (float)(total / Count); } }
    public float FramesPerSecond { get { return AverageMilliseconds <= 0 ? 0 : 1000f / AverageMilliseconds; } }
    public float PeakMilliseconds
    {
        get { float peak = 0; for (int i = 0; i < Count; i++) peak = Math.Max(peak, samples[i]); return peak; }
    }
    public RuntimeConsoleMetrics(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
        samples = new float[capacity];
    }
    public void AddFrame(float milliseconds)
    {
        if (milliseconds <= 0 || float.IsNaN(milliseconds) || float.IsInfinity(milliseconds)) return;
        if (Count == samples.Length) total -= samples[next];
        else Count++;
        samples[next] = milliseconds;
        total += milliseconds;
        next = (next + 1) % samples.Length;
    }
    public float GetOldest(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException("index");
        return samples[((Count == samples.Length ? next : 0) + index) % samples.Length];
    }
}
