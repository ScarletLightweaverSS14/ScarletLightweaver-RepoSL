using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.RTS;

/// <summary>
/// Generation settings for an isolated RTS battlefield. Uses the expedition biome samplers.
/// </summary>
[Prototype]
public sealed partial class RtsBattlefieldPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public int Size = 96;

    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Terrain;

    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Vegetation;

    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> ClearingTile;

    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> PathTile;

    [DataField(required: true)]
    public EntProtoId BlueHeadquarters;

    [DataField(required: true)]
    public EntProtoId RedHeadquarters;

    [DataField(required: true)]
    public EntProtoId BlueUnit;

    [DataField(required: true)]
    public EntProtoId RedUnit;

    [DataField]
    public int StartingUnits = 4;

    [DataField]
    public int BaseInset = 12;

    [DataField]
    public int ClearingRadius = 9;

    [DataField]
    public int PathHalfWidth = 3;
}
