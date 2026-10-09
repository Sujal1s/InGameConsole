using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

public partial class InGameConsole
{
    [Header("Suggestions and Performance")]
    public bool showCommandSuggestions = true;
    public bool showPerformancePanel = true;
    [Tooltip("Target used for the frame-time budget line, not a frame-rate limiter.")]
    [Range(15, 240)] public int targetFPS = 60;

    private const float SuggestionRowHeight = 60;
    private readonly List<KeyValuePair<string, Command>> suggestions = new List<KeyValuePair<string, Command>>();
    private readonly RuntimeConsoleMetrics metrics = new RuntimeConsoleMetrics(240);
    private int suggestionIndex;
    private string lastSuggestionInput = "";
    private bool suggestionsDismissed;
    private Vector2 suggestionScroll, performanceScroll;
    private float nextMemorySample;
    private long allocatedBytes, reservedBytes, managedBytes;

    private void SamplePerformance()
    {
        metrics.AddFrame(Time.unscaledDeltaTime * 1000f);
        if (Time.unscaledTime < nextMemorySample) return;
        nextMemorySample = Time.unscaledTime + .5f;
        allocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
        reservedBytes = Profiler.GetTotalReservedMemoryLong();
        managedBytes = Profiler.GetMonoUsedSizeLong();
    }

    private void RefreshSuggestions()
    {
        if (input != lastSuggestionInput)
        {
            lastSuggestionInput = input;
            suggestionIndex = 0;
            suggestionScroll = Vector2.zero;
            suggestionsDismissed = false;
        }
        suggestions.Clear();
        string text = input.TrimStart();
        int separator = text.IndexOfAny(new[] { ' ', '\t' });
        string token = separator < 0 ? text : text.Substring(0, separator);
        foreach (KeyValuePair<string, Command> pair in commands)
        {
            bool match = separator < 0 ? pair.Key.StartsWith(token, StringComparison.OrdinalIgnoreCase) :
                string.Equals(pair.Key, token, StringComparison.OrdinalIgnoreCase);
            if (match) suggestions.Add(pair);
        }
        suggestionIndex = Math.Max(0, Math.Min(suggestionIndex, suggestions.Count - 1));
    }

    private bool SuggestionsVisible()
    {
        return showCommandSuggestions && !suggestionsDismissed && suggestions.Count > 0 &&
            (focusInput || GUI.GetNameOfFocusedControl() == InputName);
    }

    private float GetSuggestionHeight(float height)
    {
        if (!SuggestionsVisible()) return 0;
        int visibleRows = Mathf.Clamp(Mathf.FloorToInt((height - 202 - 65 - 23) / SuggestionRowHeight), 1, 4);
        return 23 + Mathf.Min(visibleRows, suggestions.Count) * SuggestionRowHeight;
    }

    private void AcceptSuggestion(int index, bool useExample)
    {
        if (index < 0 || index >= suggestions.Count) return;
        var suggestion = suggestions[index];
        // Tab on an argument hint must not erase arguments already being edited.
        if (useExample) input = suggestion.Value.example;
        else if (input.TrimStart().IndexOfAny(new[] { ' ', '\t' }) < 0)
            input = suggestion.Key + (suggestion.Value.usage == suggestion.Key ? "" : " ");
        focusInput = true;
        suggestionsDismissed = false;
    }

    private void DrawSuggestions(Rect area)
    {
        if (area.height <= 0 || !SuggestionsVisible()) return;
        Fill(area, Line);
        GUI.Label(new Rect(area.x + 8, area.y, area.width - 16, 23), "Suggestions                                      Tab to complete", muted);
        Rect view = new Rect(area.x + 1, area.y + 23, area.width - 2, area.height - 24);
        float width = view.width - 18;
        suggestionScroll = GUI.BeginScrollView(view, suggestionScroll, new Rect(0, 0, width, suggestions.Count * SuggestionRowHeight), false, true, GUIStyle.none, scrollTrack);
        for (int i = 0; i < suggestions.Count; i++)
        {
            float y = i * SuggestionRowHeight;
            if (y + SuggestionRowHeight < suggestionScroll.y || y > suggestionScroll.y + view.height) continue;
            var pair = suggestions[i];
            Fill(new Rect(0, y, width, SuggestionRowHeight - 1), i == suggestionIndex ? new Color(.21f, .205f, .19f, 1) : Surface);
            GUI.Label(new Rect(8, y + 2, width - 100, 20), pair.Value.usage, mono);
            GUI.Label(new Rect(8, y + 22, width - 16, 18), pair.Value.description, muted);
            GUI.Label(new Rect(8, y + 40, width - 16, 18), "e.g. " + pair.Value.example, muted);
            if (GUI.Button(new Rect(width - 82, y + 2, 76, 22), "Example", button)) AcceptSuggestion(i, true);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && new Rect(0, y, width - 86, SuggestionRowHeight).Contains(e.mousePosition))
            { suggestionIndex = i; AcceptSuggestion(i, false); e.Use(); }
        }
        GUI.EndScrollView();
    }

    private void DrawPerformance(Rect area)
    {
        Fill(area, Surface);
        Fill(new Rect(area.x, area.y, 1, area.height), Line);
        float width = area.width - 12;
        performanceScroll = GUI.BeginScrollView(area, performanceScroll, new Rect(0, 0, width, 414 + WatchesHeight()), false, false, GUIStyle.none, scrollTrack);
        GUI.Label(new Rect(16, 8, width - 32, 22), "PERFORMANCE", section);
        GUI.Label(new Rect(16, 34, 115, 49), metrics.FramesPerSecond.ToString("0"), number);
        GUI.Label(new Rect(134, 48, width - 146, 28), "fps", muted);
        GUI.Label(new Rect(16, 85, width - 32, 20), "Rolling average · " + metrics.Count + " frames", muted);
        Fill(new Rect(16, 118, width - 32, 1), Line);
        GUI.Label(new Rect(16, 129, width - 32, 22), "Frame time", label);
        Rect chart = new Rect(16, 159, width - 32, 82);
        float budget = 1000f / Mathf.Max(1, targetFPS);
        float ceiling = Mathf.Max(budget * 2, metrics.PeakMilliseconds);
        Fill(chart, Background);
        for (int line = 1; line < 4; line++) Fill(new Rect(chart.x, chart.y + chart.height * line / 4, chart.width, 1), Surface);
        if (Event.current.type == EventType.Repaint)
        {
            int bars = Mathf.Min(metrics.Count, Mathf.FloorToInt(chart.width));
            for (int i = 0; i < bars; i++)
            {
                int from = i * metrics.Count / bars, to = (i + 1) * metrics.Count / bars;
                float value = 0;
                for (int j = from; j < to; j++) value = Mathf.Max(value, metrics.GetOldest(j));
                float h = value / ceiling * chart.height;
                Color color = value > budget ? Amber : new Color(.58f, .65f, .59f, 1);
                Fill(new Rect(chart.x + i * chart.width / bars, chart.yMax - h, Mathf.Max(1, chart.width / bars), h), new Color(color.r, color.g, color.b, .20f));
                Fill(new Rect(chart.x + i * chart.width / bars, chart.yMax - h, Mathf.Max(1, chart.width / bars), 1.5f), color);
            }
        }
        float budgetY = chart.yMax - budget / ceiling * chart.height;
        for (float x = chart.x; x < chart.xMax; x += 7) Fill(new Rect(x, budgetY, Mathf.Min(3, chart.xMax - x), 1), Amber);
        GUI.Label(new Rect(16, 247, width - 32, 20), metrics.AverageMilliseconds.ToString("0.0") + " ms avg   /   " + metrics.PeakMilliseconds.ToString("0.0") + " peak", mono);
        GUI.Label(new Rect(16, 270, width - 32, 18), "Target " + targetFPS + " fps · " + budget.ToString("0.0") + " ms", muted);
        Fill(new Rect(16, 302, width - 32, 1), Line);
        GUI.Label(new Rect(16, 312, width - 32, 22), new GUIContent("Memory", "Unity counters, refreshed twice per second. Managed memory overlaps Unity allocations. Editor readings include Editor overhead; zero/unavailable counters show N/A."), label);
        DrawMemoryRow(width, 340, "Allocated", allocatedBytes);
        DrawMemoryRow(width, 362, "Reserved", reservedBytes);
        DrawMemoryRow(width, 384, "Managed", managedBytes);
        DrawWatches(width, 414);
        GUI.EndScrollView();
    }

    private void DrawMemoryRow(float width, float y, string name, long bytes)
    {
        GUI.Label(new Rect(16, y, 80, 22), name, muted);
        GUI.Label(new Rect(94, y, width - 110, 22), MemoryText(bytes), rightLabel);
    }

    private static string MemoryText(long bytes)
    { return bytes <= 0 ? "N/A" : (bytes / (1024d * 1024d)).ToString("0.0") + " MiB"; }
}
