#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

public static class RuntimeConsoleInstaller
{
    [MenuItem("Tools/Runtime Console/Create Console")]
    public static void CreateConsole()
    {
        GameObject root = new GameObject("Runtime Console");
        Undo.RegisterCreatedObjectUndo(root, "Create Runtime Console");
        root.AddComponent<InGameConsole>();
        Selection.activeGameObject = root;
    }

    [MenuItem("Tools/Runtime Console/Create Wired Demo")]
    public static void CreateDemo()
    {
        GameObject root = new GameObject("Runtime Console Demo");
        Undo.RegisterCreatedObjectUndo(root, "Create Runtime Console Demo");
        root.AddComponent<InGameConsole>().showConsoleOnStart = true;
        var player = root.AddComponent<ConsoleDemoPlayer>();
        var binding = root.AddComponent<GameConsoleCommands>();
        UnityEventTools.AddPersistentListener(binding.onDie, player.Die);
        var respawn = new GameConsoleCommands.CommandBinding {
            command = "respawn", usage = "respawn", description = "Reset demo player health", example = "respawn"
        };
        UnityEventTools.AddPersistentListener(respawn.onExecute, player.Respawn);
        binding.additionalCommands.Add(respawn);
        var heal = new GameConsoleCommands.CommandBinding {
            command = "heal", usage = "heal <amount>", description = "Heal the demo player", example = "heal 25"
        };
        UnityEventTools.AddPersistentListener<string>(heal.onArguments, player.Heal);
        binding.additionalCommands.Add(heal);
        Selection.activeGameObject = root;
    }
}
#endif
