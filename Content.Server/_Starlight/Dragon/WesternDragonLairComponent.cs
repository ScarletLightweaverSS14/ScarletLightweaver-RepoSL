namespace Content.Server._Starlight.Dragon;

/// <summary>Records a generated lair on an expedition map; generation retries never respawn its boss.</summary>
[RegisterComponent]
public sealed partial class WesternDragonLairComponent : Component
{
    [DataField] public Vector2i Origin;
    [DataField] public Vector2i Entrance;
    [DataField] public Vector2i ApproachStart;
}
