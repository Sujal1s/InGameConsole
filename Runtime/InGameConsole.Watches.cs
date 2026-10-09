using System;
using UnityEngine;

public partial class InGameConsole
{
    [Header("Live Watches")]
    public bool expandWatches = true;
    [Range(.05f, 2f)] public float watchRefreshInterval = .2f;
    private readonly RuntimeConsoleWatches watches = new RuntimeConsoleWatches();
    private float nextWatchSample;

    /// <summary>Register on the main thread. Dispose the returned handle when its owner disables.</summary>
    public IDisposable RegisterWatch(string name, Func<object> read, string format = null)
    {
        nextWatchSample = 0;
        return watches.Register(name, read, format);
    }

    private void SampleWatches()
    {
        if (!visible || !showPerformancePanel || !expandWatches || Time.unscaledTime < nextWatchSample) return;
        nextWatchSample = Time.unscaledTime + Mathf.Max(.05f, watchRefreshInterval);
        watches.Sample(Time.unscaledTimeAsDouble);
    }

    private float WatchesHeight()
    {
        return 45 + (expandWatches ? watches.Items.Count == 0 ? 65 : watches.Items.Count * 49 + 24 : 0);
    }

    private void DrawWatches(float width, float y)
    {
        Fill(new Rect(16, y + 8, width - 32, 1), Line);
        if (GUI.Button(new Rect(12, y + 16, width - 24, 27),
            (expandWatches ? "−  " : "+  ") + "Watches  (" + watches.Items.Count + ")", tabActive))
        { expandWatches = !expandWatches; nextWatchSample = 0; }
        if (!expandWatches) return;
        y += 48;
        if (watches.Items.Count == 0)
        {
            GUI.Label(new Rect(16, y, width - 32, 60), "No watches yet. Add Console Variable Watches to a scene object, or call RegisterWatch().", detail);
            return;
        }
        foreach (RuntimeConsoleWatches.Watch watch in watches.Items)
        {
            bool changed = Time.unscaledTimeAsDouble - watch.ChangedAt < .8;
            if (changed) Fill(new Rect(12, y, width - 24, 46), new Color(.23f, .22f, .18f, 1));
            GUI.Label(new Rect(16, y + 1, width - 32, 19), new GUIContent(watch.Name, watch.Name), muted);
            Color old = GUI.color;
            GUI.color = watch.HasError ? Red : changed ? Accent : Color.white;
            GUI.Label(new Rect(16, y + 21, width - 32, 23), new GUIContent(FirstLine(watch.Value), watch.Value), mono);
            GUI.color = old;
            Fill(new Rect(16, y + 48, width - 32, 1), Line);
            y += 49;
        }
        GUI.Label(new Rect(16, y + 4, width - 32, 20), "Updates every " + watchRefreshInterval.ToString("0.##") + " s", muted);
    }
}
