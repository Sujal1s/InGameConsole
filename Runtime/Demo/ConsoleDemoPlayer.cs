using System.Globalization;
using UnityEngine;

/// <summary>Small demonstration. Replace its Inspector event targets with your real player.</summary>
public class ConsoleDemoPlayer : MonoBehaviour
{
    [SerializeField] private int health = 100;
    public int Health { get { return health; } }
    public bool IsDead { get { return health <= 0; } }
    private InGameConsole watchConsole;
    private System.IDisposable healthWatch, stateWatch;

    private void Update()
    {
        if (InGameConsole.Instance == watchConsole) return;
        ReleaseWatches();
        watchConsole = InGameConsole.Instance;
        if (watchConsole == null) return;
        healthWatch = watchConsole.RegisterWatch("Demo.Health", () => Health);
        stateWatch = watchConsole.RegisterWatch("Demo.State", () => IsDead ? "Dead" : "Alive");
    }
    private void OnDisable() { ReleaseWatches(); }
    private void ReleaseWatches()
    {
        healthWatch?.Dispose(); stateWatch?.Dispose();
        healthWatch = stateWatch = null;
        watchConsole = null;
    }

    public void Die()
    {
        if (IsDead) { Debug.Log("Demo player is already dead. Type respawn."); return; }
        health = 0;
        Debug.Log("Die() invoked: demo player health is now 0. Type respawn to reset.");
        // Your real player could trigger a death animation, disable movement, etc.
    }

    public void Respawn()
    {
        health = 100;
        Debug.Log("Respawn() invoked: demo player health is now 100.");
    }

    public void Heal(string arguments)
    {
        int amount;
        if (!int.TryParse(arguments, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0)
        { Debug.LogWarning("Usage: heal <positive integer>. Example: heal 25"); return; }
        if (IsDead) { Debug.LogWarning("Demo player is dead. Type respawn before healing."); return; }
        health = (int)System.Math.Min(100L, (long)health + amount);
        Debug.Log("Heal(" + amount + ") invoked: demo player health is " + health + ".");
    }
}
