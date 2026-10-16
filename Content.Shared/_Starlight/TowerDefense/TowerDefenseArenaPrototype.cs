using Robust.Shared.Prototypes;
using Content.Shared.Roles;

namespace Content.Shared._Starlight.TowerDefense;

[Prototype]
public sealed partial class TowerDefenseArenaPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public int Width = 13;
    [DataField] public int Height = 9;
    [DataField(required: true)] public List<Vector2i> Route = new();
    [DataField(required: true)] public List<TowerDefenseWave> Waves = new();
    [DataField] public int StartingCash = 100;
    [DataField] public int BaseHealth = 5;
    [DataField] public float WaveBreak = 5;
    [DataField(required: true)] public List<ProtoId<TowerDefenseTowerPrototype>> Towers = new();
    [DataField] public string BuildFloor = "FloorGrass";
    [DataField] public string RoadFloor = "FloorAsphalt";
    [DataField] public string DeckFloor = "FloorSteel";
    [DataField] public TowerDefenseArenaSounds Sounds = new();
}

[DataDefinition]
public sealed partial class TowerDefenseWave
{
    [DataField] public float SpawnDelay = 2;
    [DataField(required: true)] public List<TowerDefenseSpawn> Enemies = new();
}

[DataDefinition]
public sealed partial class TowerDefenseSpawn
{
    [DataField(required: true)] public EntProtoId Prototype;
    [DataField] public int Count = 1;
}

[Prototype]
public sealed partial class TowerDefenseTowerPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField] public string Symbol = "T";
    [DataField(required: true)] public EntProtoId Entity;
    [DataField(required: true)] public ProtoId<StartingGearPrototype> Gear;
    [DataField] public int Cost = 50;
    [DataField(required: true)] public List<TowerDefenseTowerLevel> Levels = new();
}

[DataDefinition]
public sealed partial class TowerDefenseTowerLevel
{
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public EntProtoId Weapon;
    /// <summary>Native hitscan visuals copied for cosmetic shots; no projectile is fired.</summary>
    [DataField] public EntProtoId? ShotVisuals;
    /// <summary>Price paid to enter this level; level one uses the tower purchase cost.</summary>
    [DataField] public int UpgradeCost;
    [DataField] public float Damage = 10;
    [DataField] public float Cooldown = 1;
    [DataField] public float Range = 1.6f;
}
