using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Main-thread registry. Sampling is explicit; drawing never invokes getters.</summary>
public sealed class RuntimeConsoleWatches
{
    public sealed class Watch
    {
        public string Name { get; internal set; }
        public string Value { get; internal set; }
        public bool HasError { get; internal set; }
        public double ChangedAt { get; internal set; }
        internal Func<object> read;
        internal string format;
        internal bool sampled;
    }
    private sealed class Subscription : IDisposable
    {
        private RuntimeConsoleWatches registry;
        private readonly Watch watch;
        public Subscription(RuntimeConsoleWatches registry, Watch watch) { this.registry = registry; this.watch = watch; }
        public void Dispose()
        {
            if (registry == null) return;
            registry.items.Remove(watch);
            registry = null;
        }
    }
    private readonly List<Watch> items = new List<Watch>();
    public IReadOnlyList<Watch> Items { get { return items; } }
    public const int MaximumWatches = 128;

    public IDisposable Register(string name, Func<object> read, string format = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A watch needs a name.", "name");
        if (read == null) throw new ArgumentNullException("read");
        int existing = items.FindIndex(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing < 0 && items.Count >= MaximumWatches) throw new InvalidOperationException("The console supports up to 128 watches.");
        var watch = new Watch { Name = name, Value = "Waiting…", read = read, format = format, ChangedAt = double.NegativeInfinity };
        if (existing >= 0) items[existing] = watch;
        else items.Add(watch);
        return new Subscription(this, watch);
    }

    public void Sample(double now)
    {
        // A getter may register or dispose a watch; iterate a stable snapshot.
        foreach (Watch watch in items.ToArray())
        {
            if (!items.Contains(watch)) continue;
            string value;
            bool error = false;
            try
            {
                object result = watch.read();
                var formatted = result as IFormattable;
                value = result == null ? "null" : formatted != null ? formatted.ToString(watch.format, CultureInfo.InvariantCulture) : result.ToString();
                value = value ?? "null";
            }
            catch (Exception ex)
            {
                error = true;
                Exception cause = ex.InnerException ?? ex;
                value = cause.GetType().Name + ": " + cause.Message;
            }
            if (!items.Contains(watch)) continue;
            if (watch.sampled && (watch.Value != value || watch.HasError != error)) watch.ChangedAt = now;
            watch.Value = value;
            watch.HasError = error;
            watch.sampled = true;
        }
    }
}
