using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Events;

namespace Content.Server._Starlight.Voidwalker;

public sealed partial class CrystalLanceProjectileSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalLanceProjectileComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<CrystalLanceProjectileComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnPreventCollide(Entity<CrystalLanceProjectileComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.HitEntities.Contains(args.OtherEntity))
            args.Cancelled = true;
    }

    private void OnHit(Entity<CrystalLanceProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (!ent.Comp.HitEntities.Add(args.Target))
        {
            // Multiple fixtures can already have queued collisions this tick.
            args.Damage = new DamageSpecifier();
            return;
        }

        if (HasComp<MobStateComponent>(args.Target) || !TryComp<ProjectileComponent>(ent, out var projectile))
            return;

        // Let ProjectileSystem apply the impact, then stop even if the obstacle has no Damageable.
        projectile.Hits = projectile.MaximumHits;
        projectile.ProjectileSpent = true;
    }
}
