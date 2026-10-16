using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.TowerDefense;

[Serializable, NetSerializable]
public enum TowerDefenseUiKey : byte { Key }

[Serializable, NetSerializable]
public enum TowerDefensePhase : byte { Ready, Running, Break, Victory, Defeat }

[Serializable, NetSerializable]
public enum TowerDefenseAction : byte { Start, Place, Reset, Upgrade }

[Serializable, NetSerializable]
public sealed class TowerDefenseMessage(TowerDefenseAction action, Vector2i tile = default,
    string? tower = null, int expectedLevel = 0) : BoundUserInterfaceMessage
{
    public TowerDefenseAction Action = action;
    public Vector2i Tile = tile;
    public string? Tower = tower;
    // Reject stale/repeated requests instead of accidentally purchasing two upgrades.
    public int ExpectedLevel = expectedLevel;
}

[Serializable, NetSerializable]
public sealed class TowerDefenseFeedbackMessage(string message) : BoundUserInterfaceMessage
{
    public string Message = message;
}

/// <summary>Server-provided shop or placed-tower details. Level is one-based.</summary>
[Serializable, NetSerializable]
public sealed class TowerDefenseTowerUiData
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string LevelName = string.Empty;
    public string Symbol = string.Empty;
    public int Level;
    public int MaxLevel;
    public int Cost;
    public int? UpgradeCost;
    public float Damage;
    public float Range;
    public float Cooldown;
}

[Serializable, NetSerializable]
public sealed class TowerDefenseUiState : BoundUserInterfaceState
{
    public int Width;
    public int Height;
    public int Cash;
    public int BaseHealth;
    public int Wave;
    public int TotalWaves;
    public int Alive;
    public int Remaining;
    public int BreakSeconds;
    public bool BossAlive;
    public TowerDefensePhase Phase;
    public HashSet<Vector2i> Road = new();
    public Dictionary<Vector2i, TowerDefenseTowerUiData> Towers = new();
    public List<TowerDefenseTowerUiData> Catalog = new();
    public Vector2i Start;
    public Vector2i End;
}
