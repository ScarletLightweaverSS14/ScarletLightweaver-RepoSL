namespace Content.Server._Starlight.Voidwalker;

/// <summary>Restricts the existing piercing projectile to distinct mobs, stopping it at cover.</summary>
[RegisterComponent]
public sealed partial class CrystalLanceProjectileComponent : Component
{
    public readonly HashSet<EntityUid> HitEntities = new();
}
