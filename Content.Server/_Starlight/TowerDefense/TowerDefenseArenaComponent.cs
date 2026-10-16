using Content.Shared._Starlight.TowerDefense;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.TowerDefense;

[RegisterComponent]
public sealed partial class TowerDefenseArenaComponent : Component
{
    [DataField] public ProtoId<TowerDefenseArenaPrototype> Arena = "TowerDefenseFirstWave";
    public EntityUid Grid;
    public HashSet<Vector2i> Road = new();
    public Dictionary<EntityUid, int> Enemies = new();
    public Dictionary<Vector2i, EntityUid> Towers = new();
    public Queue<EntProtoId> Pending = new();
    public TowerDefensePhase Phase;
    public int Cash;
    public int BaseHealth;
    public int Wave;
    public float SpawnTimer;
    public float BreakTimer;
    public bool UiDirty = true;
}

[RegisterComponent]
public sealed partial class TowerDefenseTowerComponent : Component
{
    public ProtoId<TowerDefenseTowerPrototype> Definition;
    /// <summary>Zero-based index into the prototype's levels.</summary>
    public int Level;
    public EntityUid? Weapon;
    public EntityUid? Target;
    public float AttackTimer;
}
