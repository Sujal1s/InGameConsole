using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>A self-contained desktop debug console. No Canvas or packages required.</summary>
public partial class InGameConsole : MonoBehaviour
{
    public static InGameConsole Instance { get; private set; }
    public enum ConsolePosition { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight, Custom }

    [Header("Console Settings")]
    public KeyCode toggleKey = KeyCode.Backslash;
    [Min(1)] public int maxLogEntries = 1000;
    public bool showConsoleOnStart;
    [Tooltip("Release builds can opt in by disabling this setting.")]
    public bool developmentBuildsOnly = true;
    public bool unlockCursorWhenOpen = true;

    [Header("Console Position")]
    public ConsolePosition position = ConsolePosition.TopLeft;
    public float positionOffsetX, positionOffsetY;
    public float customPositionX = 10, customPositionY = 10;

    [Header("Console Size")]
    [Range(.30f, 1)] public float consoleWidth = .85f;
    [Range(.15f, .90f)] public float consoleHeight = .65f;
    [Header("Resize Settings")]
    public bool allowResize = true;
    [Range(.20f, .90f)] public float minimumWidth = .30f;
    [Range(.30f, 1)] public float maximumWidth = 1;
    [Range(.10f, .50f)] public float minimumHeight = .20f;
    [Range(.30f, 1)] public float maximumHeight = .90f;

    [Header("Appearance")]
    public Font consoleFont;
    [Range(8, 24)] public int fontSize = 14;
    public bool showTimestamp = true;
    [Tooltip("Show the selected entry's stack trace in the detail pane.")]
    public bool showStackTrace = true;
    [Range(.75f, 2)] public float uiScale = 1;

    private enum Kind { Info, Warning, Error, Command, System }
    private sealed class Entry
    {
        public string message, stack, time;
        public Kind kind;
    }
    private sealed class Row
    {
        public Entry entry;
        public int count = 1;
    }
    private sealed class Command
    {
        public string usage, description, example;
        public Action<string> run;
    }

    private readonly object queueLock = new object();
    private readonly Queue<Entry> pending = new Queue<Entry>();
    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<Row> rows = new List<Row>();
    private readonly List<string> history = new List<string>();
    private readonly SortedDictionary<string, Command> commands = new SortedDictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Texture2D> textures = new List<Texture2D>();
    private const int QueueLimit = 4096;
    private const string InputName = "InGameConsole.Command";
    private const string SearchName = "InGameConsole.Search";
    private bool visible, subscribed, dirty = true, follow = true, collapse = true;
    private bool info = true, warnings = true, errors = true, focusInput;
    private int dropped, historyIndex, cachedSize, infoCount, warningCount, errorCount;
    private Font cachedFont;
    private string input = "", search = "", draft = "", notice = "Ready";
    private float noticeUntil, frameTime;
    private Vector2 logScroll, detailScroll;
    private Entry selected;
    private Rect window;
    private bool dragging, resizing, cursorCaptured;
    private int pointerControl;
    private Vector2 dragStart;
    private Rect startWindow;
    private CursorLockMode previousLock;
    private bool previousCursor;
    private GUIStyle label, muted, title, button, activeButton, field, detail;
    private GUIStyle mono, number, rightLabel, section, scrollTrack, scrollThumb, tabStyle, tabActive;
    private Font interfaceFont, codeFont;

    private static readonly Color Background = new Color(.086f, .09f, .094f, .99f);
    private static readonly Color Surface = new Color(.112f, .116f, .12f, 1);
    private static readonly Color Line = new Color(.22f, .23f, .24f, 1);
    private static readonly Color Text = new Color(.91f, .90f, .87f, 1);
    private static readonly Color Accent = new Color(.81f, .75f, .61f, 1);
    private static readonly Color Amber = new Color(.89f, .69f, .39f, 1);
    private static readonly Color Red = new Color(.94f, .49f, .43f, 1);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        // Put the component on a dedicated root object to persist between scenes.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        InstallCommands();
        if (developmentBuildsOnly && !Application.isEditor && !Debug.isDebugBuild)
        { enabled = false; return; }
        if (showConsoleOnStart) OpenConsole();
        Enqueue("Console ready. Type help to see " + commands.Count + " commands.", "", Kind.System);
    }

    private void OnEnable()
    {
        if (Instance != this || (developmentBuildsOnly && !Application.isEditor && !Debug.isDebugBuild)) return;
        Application.logMessageReceivedThreaded += CaptureUnityLog;
        subscribed = true;
    }

    private void OnDisable()
    {
        if (subscribed) Application.logMessageReceivedThreaded -= CaptureUnityLog;
        subscribed = false;
        CloseConsole();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ReleaseTextures();
        if (interfaceFont != null) Destroy(interfaceFont);
        if (codeFont != null) Destroy(codeFont);
    }

    private void Update()
    {
        frameTime = Mathf.Lerp(frameTime, Time.unscaledDeltaTime, .1f);
        SamplePerformance();
        SampleWatches();
        lock (queueLock)
        {
            // Limit work per frame during log floods. Capture remains bounded too.
            int budget = 256;
            while (pending.Count > 0 && budget-- > 0) { entries.Add(pending.Dequeue()); dirty = true; }
            if (dropped > 0)
            {
                entries.Add(new Entry { message = dropped + " messages dropped: capture queue was full.", stack = "", time = DateTime.Now.ToString("HH:mm:ss.fff"), kind = Kind.Warning });
                dropped = 0;
                dirty = true;
            }
        }
        int excess = entries.Count - Mathf.Max(1, maxLogEntries);
        if (excess > 0) { entries.RemoveRange(0, excess); dirty = true; }
        if (selected != null && !entries.Contains(selected)) selected = null;
    }

    private void CaptureUnityLog(string message, string stack, LogType type)
    {
        Kind kind = type == LogType.Warning ? Kind.Warning :
            type == LogType.Log ? Kind.Info : Kind.Error;
        Enqueue(message, stack, kind);
    }

    // This path is safe for worker threads and does not call Unity APIs.
    private void Enqueue(string message, string stack, Kind kind)
    {
        lock (queueLock)
        {
            if (pending.Count >= QueueLimit) { pending.Dequeue(); dropped++; }
            pending.Enqueue(new Entry { message = message ?? "", stack = stack ?? "", kind = kind, time = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) });
        }
    }

    public void Log(string message) { Enqueue(message, "", Kind.Info); }
    public void LogNetwork(string message) { Log("[Netcode] " + message); }
    public void LogWarning(string message) { Enqueue(message, "", Kind.Warning); }
    public void LogError(string message) { Enqueue(message, "", Kind.Error); }
    public void LogException(string message) { Enqueue(message, "", Kind.Error); }
    public bool IsConsoleVisible() { return visible; }
    public void ToggleConsole() { if (visible) CloseConsole(); else OpenConsole(); }

    public void OpenConsole()
    {
        if (!isActiveAndEnabled || Instance != this) return;
        if (developmentBuildsOnly && !Application.isEditor && !Debug.isDebugBuild) return;
        visible = true;
        focusInput = true;
        suggestionsDismissed = false;
        if (unlockCursorWhenOpen && !cursorCaptured)
        {
            previousLock = Cursor.lockState;
            previousCursor = Cursor.visible;
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void CloseConsole()
    {
        visible = false;
        dragging = resizing = false;
        if (pointerControl != 0 && GUIUtility.hotControl == pointerControl) GUIUtility.hotControl = 0;
        pointerControl = 0;
        if (!cursorCaptured) return;
        Cursor.lockState = previousLock;
        Cursor.visible = previousCursor;
        cursorCaptured = false;
    }

    public void ClearConsole()
    {
        lock (queueLock) { pending.Clear(); dropped = 0; }
        entries.Clear();
        rows.Clear();
        selected = null;
        logScroll = detailScroll = Vector2.zero;
        dirty = true;
        SetNotice("Logs cleared");
    }

    /// <summary>Register on the main thread after this component's Awake (for example in Start).</summary>
    public void RegisterCommand(string name, string usage, string description, Action<string> handler, string example = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
            throw new ArgumentException("Command names must be one non-empty token.", "name");
        if (handler == null) throw new ArgumentNullException("handler");
        commands[name] = new Command { usage = usage ?? name, description = description ?? "", run = handler, example = example ?? name };
    }

    public bool ContainsCommand(string name) { return name != null && commands.ContainsKey(name); }

    // Handler identity prevents a scene component from removing another owner's command.
    public bool UnregisterCommand(string name, Action<string> handler)
    {
        Command command;
        return name != null && commands.TryGetValue(name, out command) && command.run == handler && commands.Remove(name);
    }

    private void InstallCommands()
    {
        RegisterCommand("help", "help", "List commands", a => {
            foreach (KeyValuePair<string, Command> pair in commands)
                Enqueue(pair.Value.usage + "  -  " + pair.Value.description, "", Kind.System);
        });
        RegisterCommand("clear", "clear", "Clear captured logs", a => ClearConsole());
        RegisterCommand("close", "close", "Close the console", a => CloseConsole());
        RegisterCommand("log", "log <message>", "Write a Unity log", a => Debug.Log(a), "log Hello world");
        RegisterCommand("warn", "warn <message>", "Write a Unity warning", a => Debug.LogWarning(a), "warn Connection is slow");
        RegisterCommand("error", "error <message>", "Write a Unity error", a => Debug.LogError(a), "error Test error");
        RegisterCommand("netcode", "netcode <message>", "Write a network-tagged log", LogNetwork, "netcode Connected to host");
        RegisterCommand("sort<int>", "sort<int> [3,2,1]", "Sort integer values", SortIntegers, "sort<int> [3,-2,1]");
        RegisterCommand("copy", "copy", "Copy the filtered logs", a => CopyVisible());
        RegisterCommand("export", "export", "Save filtered logs as a text file", a => ExportVisible());
        RegisterCommand("sysinfo", "sysinfo", "Show runtime information", a => Enqueue(
            Application.productName + " " + Application.version + " | Unity " + Application.unityVersion +
            " | " + Application.platform + " | " + Screen.width + "x" + Screen.height, "", Kind.System));
    }

    private void SortIntegers(string args)
    {
        args = args.Trim();
        if (!args.StartsWith("[", StringComparison.Ordinal) || !args.EndsWith("]", StringComparison.Ordinal))
        { LogWarning("Usage: sort<int> [3,2,1]"); return; }
        string body = args.Substring(1, args.Length - 2).Trim();
        var numbers = new List<int>();
        if (body.Length > 0)
        {
            foreach (string part in body.Split(','))
            {
                int number;
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                { LogWarning("Invalid integer: " + part + ". Use values such as [3,-2,1]."); return; }
                numbers.Add(number);
            }
        }
        numbers.Sort();
        Enqueue("[" + string.Join(", ", numbers.ConvertAll(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()) + "]", "", Kind.System);
    }

    private void ExecuteCommand()
    {
        string text = input.Trim();
        if (text.Length == 0) return;
        if (history.Count == 0 || history[history.Count - 1] != text) history.Add(text);
        if (history.Count > 100) history.RemoveAt(0);
        historyIndex = history.Count;
        draft = input = "";
        lastSuggestionInput = input;
        suggestionsDismissed = true;
        focusInput = true;
        Enqueue("> " + text, "", Kind.Command);
        int split = text.IndexOfAny(new[] { ' ', '\t' });
        string name = split < 0 ? text : text.Substring(0, split);
        string args = split < 0 ? "" : text.Substring(split + 1).Trim();
        Command command;
        if (!commands.TryGetValue(name, out command)) { LogWarning("Unknown command: " + name + ". Type help or press Tab to complete a command."); return; }
        try { command.run(args); }
        catch (Exception ex) { Enqueue("Command failed: " + ex.Message, ex.ToString(), Kind.Error); }
    }

    private void CompleteCommand()
    {
        RefreshSuggestions();
        if (suggestions.Count > 0) AcceptSuggestion(suggestionIndex, false);
        else SetNotice("No matching command");
    }

    private void HandleKeys()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;
        if (e.keyCode == toggleKey) { ToggleConsole(); e.Use(); return; }
        if (!visible) return;
        if (e.keyCode == KeyCode.Escape)
        {
            if (SuggestionsVisible()) suggestionsDismissed = true;
            else CloseConsole();
            e.Use(); return;
        }
        if ((e.control || e.command) && e.keyCode == KeyCode.F) { GUI.FocusControl(SearchName); focusInput = false; e.Use(); return; }
        if (GUI.GetNameOfFocusedControl() != InputName) return;
        RefreshSuggestions();
        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { ExecuteCommand(); e.Use(); }
        else if (e.keyCode == KeyCode.Tab) { CompleteCommand(); e.Use(); }
        else if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
        {
            if (SuggestionsVisible() && input.Trim().Length > 0 && input.TrimStart().IndexOfAny(new[] { ' ', '\t' }) < 0 && !e.alt)
            {
                suggestionIndex = (suggestionIndex + (e.keyCode == KeyCode.UpArrow ? -1 : 1) + suggestions.Count) % suggestions.Count;
                suggestionScroll.y = suggestionIndex * SuggestionRowHeight;
                e.Use(); return;
            }
            if (historyIndex == history.Count) draft = input;
            historyIndex = Mathf.Clamp(historyIndex + (e.keyCode == KeyCode.UpArrow ? -1 : 1), 0, history.Count);
            input = historyIndex == history.Count ? draft : history[historyIndex];
            suggestionsDismissed = true;
            lastSuggestionInput = input;
            focusInput = true;
            e.Use();
        }
    }

    private void RebuildRows()
    {
        if (!dirty) return;
        dirty = false;
        rows.Clear();
        infoCount = warningCount = errorCount = 0;
        Entry previous = null;
        foreach (Entry entry in entries)
        {
            if (entry.kind == Kind.Error) errorCount++;
            else if (entry.kind == Kind.Warning) warningCount++;
            else infoCount++;
            if (!Matches(entry)) { previous = null; continue; }
            if (collapse && previous != null && previous.kind == entry.kind && previous.message == entry.message && previous.stack == entry.stack)
                rows[rows.Count - 1].count++;
            else rows.Add(new Row { entry = entry });
            previous = entry;
        }
    }

    private bool Matches(Entry e)
    {
        if (e.kind == Kind.Error ? !errors : e.kind == Kind.Warning ? !warnings : !info) return false;
        return search.Length == 0 || e.message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || e.stack.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnGUI()
    {
        if (Instance != this) return;
        HandleKeys();
        if (!visible) return;
        EnsureStyles();
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        // A minimum logical canvas keeps toolbars usable on small Game views.
        float scale = Mathf.Max(.01f, Mathf.Min(uiScale, Screen.width / 660f, Screen.height / 380f));
        float sw = Screen.width / scale, sh = Screen.height / scale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
        GUI.color = Color.white;
        GUI.depth = -1000;
        GUIStyle oldThumb = GUI.skin.verticalScrollbarThumb;
        GUI.skin.verticalScrollbarThumb = scrollThumb;
        try
        {
            window = CalculateRect(sw, sh);
            HandlePointer(sw, sh);
            RebuildRows();
            Fill(new Rect(window.x + 5, window.y + 7, window.width, window.height), new Color(0, 0, 0, .25f));
            Fill(window, Line);
            Fill(new Rect(window.x + 1, window.y + 1, window.width - 2, window.height - 2), Background);
            GUI.BeginGroup(window);
            DrawHeader(window.width);
            DrawToolbar(window.width);
            RefreshSuggestions();
            float suggestionHeight = GetSuggestionHeight(window.height);
            Rect body = new Rect(12, 124, window.width - 24, window.height - 202);
            float performanceWidth = showPerformancePanel ? Mathf.Clamp(body.width * .29f, 242, 290) : 0;
            DrawLogs(new Rect(body.x, body.y, body.width - performanceWidth, body.height - suggestionHeight));
            if (showPerformancePanel) DrawPerformance(new Rect(body.xMax - performanceWidth + 16, body.y, performanceWidth - 16, body.height));
            DrawCommandBar(window.width, window.height);
            DrawSuggestions(new Rect(body.x, body.yMax - suggestionHeight, body.width - performanceWidth, suggestionHeight));
            GUI.EndGroup();
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(GUI.tooltip))
            {
                float tw = Mathf.Min(350, window.width - 32);
                float th = detail.CalcHeight(new GUIContent(GUI.tooltip), tw - 16) + 12;
                Rect tip = new Rect(Mathf.Clamp(Event.current.mousePosition.x, window.x + 8, window.xMax - tw - 8), Mathf.Clamp(Event.current.mousePosition.y + 22, window.y + 8, window.yMax - th - 8), tw, th);
                Fill(tip, Line);
                Fill(new Rect(tip.x + 1, tip.y + 1, tw - 2, th - 2), Surface);
                GUI.Label(new Rect(tip.x + 8, tip.y + 6, tw - 16, th - 12), GUI.tooltip, detail);
            }
            if (focusInput && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(InputName);
                var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
                if (editor != null) editor.MoveTextEnd();
                focusInput = false;
            }
            // Prevent IMGUI controls behind the overlay from receiving unused pointer events.
            Event e = Event.current;
            if (window.Contains(e.mousePosition) && (e.isMouse || e.type == EventType.ScrollWheel)) e.Use();
        }
        finally { GUI.skin.verticalScrollbarThumb = oldThumb; GUI.matrix = previousMatrix; GUI.color = previousColor; GUI.depth = previousDepth; }
    }

    private void DrawHeader(float width)
    {
        Fill(new Rect(1, 1, width - 2, 48), Surface);
        GUI.Label(new Rect(18, 7, 120, 34), "Console", title);
        GUI.Label(new Rect(130, 10, 120, 28), "Runtime session", muted);
        if (GUI.Button(new Rect(width - 366, 10, 98, 28), "Performance", showPerformancePanel ? activeButton : button)) showPerformancePanel = !showPerformancePanel;
        GUI.Label(new Rect(width - 252, 10, 152, 28), entries.Count + " captured", rightLabel);
        if (GUI.Button(new Rect(width - 84, 10, 68, 28), new GUIContent("Close", "Close console (Escape)"), button)) CloseConsole();
        Fill(new Rect(1, 48, width - 2, 1), Line);
    }

    private void DrawToolbar(float width)
    {
        GUI.SetNextControlName(SearchName);
        Rect searchRect = new Rect(12, 57, width - 268, 28);
        string next = GUI.TextField(searchRect, search, 2048, field);
        if (search.Length == 0 && GUI.GetNameOfFocusedControl() != SearchName)
            GUI.Label(new Rect(22, 57, searchRect.width - 18, 28), "Search messages or stack traces...", muted);
        if (next != search) { search = next; dirty = true; logScroll = Vector2.zero; follow = false; }
        if (GUI.Button(new Rect(width - 246, 57, 30, 28), new GUIContent("×", "Reset search"), button)) { search = ""; dirty = true; }
        if (GUI.Button(new Rect(width - 210, 57, 62, 28), "Copy", button)) CopyVisible();
        if (GUI.Button(new Rect(width - 142, 57, 62, 28), "Export", button)) ExportVisible();
        if (GUI.Button(new Rect(width - 74, 57, 62, 28), "Clear", button)) ClearConsole();
        ToggleChip(new Rect(12, 92, 92, 26), "Info  " + infoCount, ref info);
        ToggleChip(new Rect(110, 92, 92, 26), "Warn  " + warningCount, ref warnings);
        ToggleChip(new Rect(208, 92, 92, 26), "Error  " + errorCount, ref errors);
        ToggleChip(new Rect(width - 226, 92, 106, 26), "Group repeats", ref collapse);
        if (GUI.Button(new Rect(width - 114, 92, 102, 26), follow ? "Following ↓" : "Follow latest", follow ? tabActive : tabStyle)) follow = !follow;
        if (follow) Fill(new Rect(width - 106, 117, 86, 1), Accent);
        RebuildRows();
    }

    private void ToggleChip(Rect rect, string text, ref bool value)
    {
        if (GUI.Button(rect, text, value ? tabActive : tabStyle)) { value = !value; dirty = true; }
        if (value) Fill(new Rect(rect.x + 8, rect.yMax - 1, rect.width - 16, 1), Accent);
    }

    private void DrawLogs(Rect area)
    {
        bool hasSelection = selected != null && Matches(selected);
        float detailsHeight = hasSelection ? Mathf.Min(150, area.height * .45f) : 0;
        Rect list = new Rect(area.x, area.y, area.width, area.height - detailsHeight);
        Fill(list, Background);
        GUI.Label(new Rect(list.x + 10, list.y, 95, 23), showTimestamp ? "TIME" : "", section);
        GUI.Label(new Rect(list.x + (showTimestamp ? 113 : 10), list.y, 62, 23), "LEVEL", section);
        GUI.Label(new Rect(list.x + (showTimestamp ? 180 : 77), list.y, list.width - 190, 23), "MESSAGE", section);
        Fill(new Rect(list.x, list.y + 26, list.width, 1), Line);
        list.y += 29; list.height = Mathf.Max(1, list.height - 29);
        float rowHeight = Mathf.Max(34, fontSize + 16);
        float contentHeight = Mathf.Max(list.height, rows.Count * rowHeight);
        float maxScroll = Mathf.Max(0, contentHeight - list.height);
        Event e = Event.current;
        if (list.Contains(e.mousePosition) &&
            ((e.type == EventType.ScrollWheel && e.delta.y < 0) ||
             (e.type == EventType.MouseDown && e.mousePosition.x >= list.xMax - 18))) follow = false;
        if (follow) logScroll.y = maxScroll;
        logScroll.y = Mathf.Clamp(logScroll.y, 0, maxScroll);
        float contentWidth = list.width - 18;
        logScroll = GUI.BeginScrollView(list, logScroll, new Rect(0, 0, contentWidth, contentHeight), false, true, GUIStyle.none, scrollTrack);
        // Draw only visible rows; full messages and traces live in the detail pane.
        int first = Mathf.Max(0, Mathf.FloorToInt(logScroll.y / rowHeight));
        int last = Mathf.Min(rows.Count, Mathf.CeilToInt((logScroll.y + list.height) / rowHeight) + 1);
        for (int i = first; i < last; i++)
        {
            Row row = rows[i];
            Entry entry = row.entry;
            Rect rect = new Rect(0, i * rowHeight, contentWidth, rowHeight);
            if (selected == entry) Fill(rect, new Color(.21f, .205f, .19f, 1));
            else if (entry.kind == Kind.Error) Fill(rect, new Color(.17f, .115f, .11f, 1));
            Fill(new Rect(8, rect.yMax - 1, contentWidth - 16, 1), new Color(.135f, .14f, .145f, 1));
            Color color = EntryColor(entry.kind);
            Fill(new Rect(0, rect.y + 13, 3, 8), color);
            float x = 10;
            if (showTimestamp) { GUI.Label(new Rect(x, rect.y, 100, rowHeight), entry.time, mono); x += 103; }
            Color old = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(x, rect.y, 64, rowHeight), entry.kind.ToString(), muted);
            GUI.color = old;
            x += 67;
            GUI.Label(new Rect(x, rect.y, Mathf.Max(0, contentWidth - x - 55), rowHeight), FirstLine(entry.message), mono);
            if (row.count > 1) GUI.Label(new Rect(contentWidth - 52, rect.y, 48, rowHeight), "x" + row.count, muted);
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            { selected = entry; detailScroll = Vector2.zero; follow = false; GUI.FocusControl(null); e.Use(); }
        }
        GUI.EndScrollView();
        if (rows.Count == 0) GUI.Label(new Rect(list.x + 18, list.y + 18, list.width - 36, 50), entries.Count == 0 ? "No logs yet. Run a command below to get started." : "No matching logs. Clear the search or enable more severity filters.", detail);
        if (hasSelection) DrawDetails(new Rect(area.x, list.yMax, area.width, detailsHeight));
    }

    private void DrawDetails(Rect area)
    {
        Fill(area, Surface);
        GUI.Label(new Rect(area.x + 10, area.y + 3, area.width - 190, 27), "SELECTED ENTRY  /  " + selected.time, muted);
        if (GUI.Button(new Rect(area.xMax - 166, area.y + 3, 124, 25), "Copy entry", button))
        { GUIUtility.systemCopyBuffer = FormatEntry(selected); SetNotice("Entry copied"); }
        if (GUI.Button(new Rect(area.xMax - 36, area.y + 3, 28, 25), "X", button)) { selected = null; return; }
        string text = selected.message + (showStackTrace && selected.stack.Length > 0 ? "\n\n" + selected.stack : "");
        Rect viewport = new Rect(area.x + 8, area.y + 33, area.width - 16, Mathf.Max(1, area.height - 39));
        float w = viewport.width - 20;
        float h = Mathf.Max(viewport.height, detail.CalcHeight(new GUIContent(text), w));
        detailScroll = GUI.BeginScrollView(viewport, detailScroll, new Rect(0, 0, w, h));
        // Discard edits: TextArea permits selecting/copying without mutating stored logs.
        GUI.TextArea(new Rect(0, 0, w, h), text, detail);
        GUI.EndScrollView();
    }

    private void DrawCommandBar(float width, float height)
    {
        float y = height - 68;
        Fill(new Rect(12, y, width - 24, 36), Surface);
        Fill(new Rect(12, y, width - 24, 1), Line);
        GUI.Label(new Rect(20, y + 3, 22, 30), ">", mono);
        GUI.SetNextControlName(InputName);
        string nextInput = GUI.TextField(new Rect(44, y + 3, width - 142, 30), input, 8192, field);
        if (nextInput != input) { input = nextInput; suggestionsDismissed = false; }
        if (input.Length == 0 && GUI.GetNameOfFocusedControl() != InputName)
            GUI.Label(new Rect(54, y + 3, width - 162, 30), "Enter a command…", muted);
        bool oldEnabled = GUI.enabled;
        GUI.enabled = !string.IsNullOrWhiteSpace(input);
        if (GUI.Button(new Rect(width - 90, y + 3, 70, 30), "Run", activeButton)) ExecuteCommand();
        GUI.enabled = oldEnabled;
        string status = Time.unscaledTime < noticeUntil ? notice : "Enter  run     Tab  complete     Alt + ↑↓  history";
        GUI.Label(new Rect(12, height - 28, width - 42, 23), status, muted);
        if (allowResize) GUI.Label(new Rect(width - 26, height - 25, 22, 22), "//", muted);
    }

    private Rect CalculateRect(float sw, float sh)
    {
        float width = Mathf.Clamp(sw * consoleWidth, 636, sw - 20);
        float height = Mathf.Clamp(sh * consoleHeight, 340, sh - 20);
        int index = (int)position;
        float x = position == ConsolePosition.Custom ? customPositionX : index % 3 == 0 ? 10 : index % 3 == 1 ? (sw - width) / 2 : sw - width - 10;
        float y = position == ConsolePosition.Custom ? customPositionY : index / 3 == 0 ? 10 : index / 3 == 1 ? (sh - height) / 2 : sh - height - 10;
        return new Rect(Mathf.Clamp(x + positionOffsetX, 0, sw - width), Mathf.Clamp(y + positionOffsetY, 0, sh - height), width, height);
    }

    private void HandlePointer(float sw, float sh)
    {
        Event e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Rect handle = new Rect(window.xMax - 24, window.yMax - 24, 24, 24);
        Rect header = new Rect(window.x, window.y, window.width - 380, 48);
        if (e.type == EventType.MouseDown && e.button == 0 && (header.Contains(e.mousePosition) || (allowResize && handle.Contains(e.mousePosition))))
        {
            resizing = allowResize && handle.Contains(e.mousePosition);
            dragging = !resizing;
            startWindow = window;
            dragStart = e.mousePosition;
            pointerControl = id;
            GUIUtility.hotControl = id;
            GUI.FocusControl(null);
            e.Use();
        }
        if (e.type == EventType.MouseDrag && (dragging || resizing))
        {
            Vector2 delta = e.mousePosition - dragStart;
            position = ConsolePosition.Custom;
            positionOffsetX = positionOffsetY = 0;
            if (dragging)
            {
                customPositionX = Mathf.Clamp(startWindow.x + delta.x, 0, sw - window.width);
                customPositionY = Mathf.Clamp(startWindow.y + delta.y, 0, sh - window.height);
            }
            else
            {
                customPositionX = startWindow.x;
                customPositionY = startWindow.y;
                float minW = Mathf.Max(636, sw * Mathf.Min(minimumWidth, maximumWidth));
                float minH = Mathf.Max(340, sh * Mathf.Min(minimumHeight, maximumHeight));
                float maxW = Mathf.Max(minW, sw * Mathf.Max(minimumWidth, maximumWidth));
                float maxH = Mathf.Max(minH, sh * Mathf.Max(minimumHeight, maximumHeight));
                consoleWidth = Mathf.Min(Mathf.Clamp(startWindow.width + delta.x, minW, maxW), sw - startWindow.x) / sw;
                consoleHeight = Mathf.Min(Mathf.Clamp(startWindow.height + delta.y, minH, maxH), sh - startWindow.y) / sh;
            }
            window = CalculateRect(sw, sh);
            e.Use();
        }
        if (e.type == EventType.MouseUp && (dragging || resizing))
        { dragging = resizing = false; GUIUtility.hotControl = 0; pointerControl = 0; e.Use(); }
    }

    private string VisibleText()
    {
        var result = new StringBuilder();
        foreach (Entry entry in entries) if (Matches(entry)) result.AppendLine(FormatEntry(entry));
        return result.ToString();
    }
    private static string FormatEntry(Entry e) { return "[" + e.time + "] [" + e.kind + "] " + e.message + (e.stack.Length == 0 ? "" : "\n" + e.stack); }
    private void CopyVisible() { GUIUtility.systemCopyBuffer = VisibleText(); SetNotice("Filtered logs copied (including repeat occurrences)"); }
    private void ExportVisible()
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, "console-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            File.WriteAllText(path, VisibleText(), Encoding.UTF8);
            SetNotice("Exported: " + path);
            Enqueue("Exported logs to " + path, "", Kind.System);
        }
        catch (Exception ex) { LogError("Export failed: " + ex.Message); SetNotice("Export failed. See error log."); }
    }
    private void SetNotice(string text) { notice = text; noticeUntil = Time.unscaledTime + 7; }
    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(new[] { '\r', '\n' });
        int length = Math.Min(end < 0 ? text.Length : end, 512);
        return text.Substring(0, length) + (length < text.Length ? " ..." : "");
    }
    private static Color EntryColor(Kind kind) { return kind == Kind.Error ? Red : kind == Kind.Warning ? Amber : kind == Kind.Command || kind == Kind.System ? Accent : Text; }
    private static void Fill(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        textures.Add(texture);
        return texture;
    }
    private void ReleaseTextures() { foreach (Texture2D t in textures) if (t != null) Destroy(t); textures.Clear(); }
    private void EnsureStyles()
    {
        if (label != null && cachedSize == fontSize && cachedFont == consoleFont) return;
        ReleaseTextures();
        cachedSize = fontSize; cachedFont = consoleFont;
        // OS fonts are not redistributed. Unity falls back to its default if unavailable.
        if (interfaceFont == null) interfaceFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Helvetica Neue", "Arial" }, 14);
        if (codeFont == null) codeFont = Font.CreateDynamicFontFromOSFont(new[] { "Cascadia Mono", "Consolas", "Menlo", "DejaVu Sans Mono" }, 13);
        Font ui = consoleFont != null ? consoleFont : interfaceFont;
        Font code = consoleFont != null ? consoleFont : codeFont;
        label = new GUIStyle(GUI.skin.label) { font = ui, fontSize = Mathf.Max(14, fontSize), richText = false, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
        label.normal.textColor = Text;
        muted = new GUIStyle(label) { fontSize = 12 };
        muted.normal.textColor = new Color(.66f, .67f, .67f, 1);
        mono = new GUIStyle(label) { font = code, fontSize = Mathf.Max(13, fontSize - 1) };
        mono.normal.textColor = new Color(.80f, .80f, .78f, 1);
        section = new GUIStyle(muted) { fontSize = 10, fontStyle = FontStyle.Bold };
        title = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = 20 };
        number = new GUIStyle(label) { fontSize = 38, fontStyle = FontStyle.Normal };
        rightLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontSize = 12 };
        Texture2D normal = MakeTexture(Surface), hover = MakeTexture(new Color(.19f, .195f, .20f, 1)), active = MakeTexture(new Color(.23f, .23f, .22f, 1));
        button = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, padding = new RectOffset(6, 6, 2, 2) };
        button.normal.background = normal; button.hover.background = hover; button.active.background = active; button.focused.background = hover;
        button.normal.textColor = muted.normal.textColor;
        button.hover.textColor = button.active.textColor = button.focused.textColor = Text;
        activeButton = new GUIStyle(button);
        activeButton.normal.background = active; activeButton.normal.textColor = Text;
        tabStyle = new GUIStyle(button);
        tabStyle.normal.background = null;
        tabActive = new GUIStyle(tabStyle);
        tabActive.normal.textColor = Text;
        field = new GUIStyle(GUI.skin.textField) { font = code, fontSize = Mathf.Max(14, fontSize), richText = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 3, 3) };
        field.normal.background = normal; field.hover.background = hover; field.focused.background = hover;
        field.normal.textColor = field.hover.textColor = field.focused.textColor = Text;
        detail = new GUIStyle(mono) { wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(4, 4, 4, 4) };
        scrollTrack = new GUIStyle(GUI.skin.verticalScrollbar) { fixedWidth = 8, border = new RectOffset(), padding = new RectOffset(), margin = new RectOffset() };
        scrollTrack.normal.background = MakeTexture(Background);
        scrollThumb = new GUIStyle(GUI.skin.verticalScrollbarThumb);
        scrollThumb.normal.background = MakeTexture(new Color(.30f, .31f, .32f, 1));
        scrollThumb.hover.background = scrollThumb.active.background = scrollThumb.normal.background;
        scrollTrack.name = "verticalScrollbar";
        // BeginScrollView obtains its thumb from the current skin; install only during OnGUI.
    }
}

