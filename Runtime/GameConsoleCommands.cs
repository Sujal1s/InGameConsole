using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Add to a scene object, then wire On Die to your player's public Die() method.</summary>
[DisallowMultipleComponent]
public class GameConsoleCommands : MonoBehaviour
{
    [Serializable] public sealed class ArgumentEvent : UnityEvent<string> { }
    [Serializable] public sealed class CommandBinding
    {
        public string command = "heal";
        public string usage = "heal <amount>";
        public string description = "Restore player health";
        public string example = "heal 25";
        [Tooltip("Invoked first, for public methods with no arguments.")]
        public UnityEvent onExecute = new UnityEvent();
        [Tooltip("Invoked next, passing the text after the command. Select a Dynamic string method.")]
        public ArgumentEvent onArguments = new ArgumentEvent();
    }

    [Header("Built-in game command: die")]
    [Tooltip("Drag your player here and choose its public Die() method.")]
    public UnityEvent onDie = new UnityEvent();

    [Header("Additional game commands")]
    public List<CommandBinding> additionalCommands = new List<CommandBinding>();

    private InGameConsole registeredConsole;
    private readonly Dictionary<string, Action<string>> owned = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase);

    // Polling also supports consoles created after this scene component is enabled.
    private void Update()
    {
        InGameConsole console = InGameConsole.Instance;
        if (console == registeredConsole) return;
        Unbind();
        if (console == null) return;
        registeredConsole = console;
        Bind("die", "die", "Invoke your player's death function", "die", args =>
        {
            if (!isActiveAndEnabled) return;
            if (!string.IsNullOrWhiteSpace(args)) { registeredConsole.LogWarning("Usage: die"); return; }
            Die();
        });
        if (additionalCommands == null) return;
        foreach (CommandBinding item in additionalCommands)
        {
            if (item == null) continue;
            CommandBinding binding = item;
            Bind(binding.command, binding.usage, binding.description, binding.example, args =>
            {
                if (!isActiveAndEnabled) return;
                binding.onExecute?.Invoke();
                binding.onArguments?.Invoke(args);
            });
        }
    }

    /// <summary>This is the function invoked when you type die into the console.</summary>
    public void Die()
    {
        // OPTION A (no code): Connect On Die in the Inspector to PlayerHealth.Die().
        onDie?.Invoke();

        // OPTION B: Add a field for your player component and call it here instead:
        // playerHealth.Die();
        // Choose one approach to avoid invoking your death logic twice.
    }

    private void Bind(string name, string usage, string description, string example, Action<string> handler)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
        { registeredConsole.LogWarning("Skipped an invalid game command name. Use one non-empty word."); return; }
        if (registeredConsole.ContainsCommand(name))
        { registeredConsole.LogWarning("Game command '" + name + "' is already registered. Only one component should own it."); return; }
        registeredConsole.RegisterCommand(name, usage, description, handler, example);
        owned.Add(name, handler);
    }

    private void OnDisable() { Unbind(); }
    private void OnDestroy() { Unbind(); }
    private void Unbind()
    {
        if (registeredConsole != null)
            foreach (KeyValuePair<string, Action<string>> pair in owned)
                registeredConsole.UnregisterCommand(pair.Key, pair.Value);
        owned.Clear();
        registeredConsole = null;
    }
}
