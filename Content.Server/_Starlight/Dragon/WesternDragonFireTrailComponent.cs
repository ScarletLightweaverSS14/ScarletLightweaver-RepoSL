namespace Content.Server._Starlight.Dragon;

[RegisterComponent]
public sealed partial class WesternDragonFireTrailComponent : Component
{
    public (EntityUid Grid, Vector2i Tile)? LastTile;
}
