using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Inspector-configured watches for public instance fields or readable properties.</summary>
public class ConsoleVariableWatches : MonoBehaviour
{
    [Serializable] public sealed class Binding
    {
        public string label = "Player.Health";
        public MonoBehaviour target;
        [Tooltip("Exact public field or property name; case sensitive. No nested paths or methods.")]
        public string member = "Health";
        [Tooltip("Optional numeric format, for example 0.0 for speed.")]
        public string format = "";
    }
    public List<Binding> bindings = new List<Binding>();
    private readonly List<IDisposable> subscriptions = new List<IDisposable>();
    private InGameConsole boundConsole;

    private void Update()
    {
        InGameConsole console = InGameConsole.Instance;
        if (console == boundConsole) return;
        Unbind();
        if (console == null) return;
        boundConsole = console;
        if (bindings == null) return;
        foreach (Binding binding in bindings)
        {
            if (binding == null) continue;
            try
            {
                if (binding.target == null) throw new ArgumentException("Assign a target component.");
                if (string.IsNullOrWhiteSpace(binding.member)) throw new ArgumentException("Enter a public member name.");
                MonoBehaviour target = binding.target;
                Type type = target.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                FieldInfo fieldInfo = type.GetField(binding.member, flags);
                PropertyInfo property = type.GetProperty(binding.member, flags);
                if (fieldInfo == null && (property == null || property.GetGetMethod() == null || property.GetIndexParameters().Length != 0))
                    throw new ArgumentException("No public field or readable non-indexed property named " + binding.member + ".");
                subscriptions.Add(console.RegisterWatch(binding.label, () => {
                    if (target == null) throw new MissingReferenceException("Watch target was destroyed.");
                    return fieldInfo != null ? fieldInfo.GetValue(target) : property.GetValue(target, null);
                }, string.IsNullOrEmpty(binding.format) ? null : binding.format));
            }
            catch (Exception ex) { console.LogWarning("Watch '" + binding.label + "': " + ex.Message); }
        }
    }
    private void OnDisable() { Unbind(); }
    private void OnDestroy() { Unbind(); }
    private void Unbind()
    {
        foreach (IDisposable subscription in subscriptions) subscription.Dispose();
        subscriptions.Clear();
        boundConsole = null;
    }
}
