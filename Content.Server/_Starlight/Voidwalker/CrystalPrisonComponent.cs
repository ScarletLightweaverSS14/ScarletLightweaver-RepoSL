using Content.Shared.Physics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Voidwalker;

/// <summary>Grants a temporary crystal cage. Durability and lifetime belong to its crystal prototype.</summary>
[RegisterComponent]
public sealed partial class CrystalPrisonComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionVoidwalkerCrystalPrison";

    [DataField]
    public EntProtoId CrystalPrototype = "VoidwalkerPrisonCrystal";

    [DataField]
    public EntProtoId? GrowthEffect = "VoidwalkerCrystalFlash";

    /// <summary>Tile offsets around the clicked tile. The center is always left open.</summary>
    [DataField]
    public List<Vector2i> Offsets =
    [
        new(-1, -1), new(0, -1), new(1, -1), new(1, 0),
        new(1, 1), new(0, 1), new(-1, 1), new(-1, 0),
    ];

    /// <summary>Skip occupied perimeter tiles instead of embedding crystals in mobs or structures.</summary>
    [DataField]
    public CollisionGroup PlacementMask = CollisionGroup.Impassable | CollisionGroup.HighImpassable |
        CollisionGroup.MidImpassable | CollisionGroup.LowImpassable | CollisionGroup.BulletImpassable;

    [DataField]
    public SoundSpecifier GrowthSound = new SoundPathSpecifier("/Audio/Effects/glass_crack2.ogg");

    [DataField]
    public LocId Callout = "voidwalker-crystal-prison-callout";

    [DataField]
    public EntityUid? ActionEntity;
}
