using System.Numerics;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Sprite;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Voidwalker;

public sealed partial class CrystalLanceSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedScaleVisualsSystem _scale = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalLanceComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CrystalLanceComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CrystalLanceComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<CrystalLanceComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CrystalLanceComponent, CrystalLanceEvent>(OnCast);
    }

    private void OnStartup(Entity<CrystalLanceComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<CrystalLanceComponent> ent, ref ComponentShutdown args)
    {
        ClearCharge(ent);
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnUnpaused(Entity<CrystalLanceComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.ReleaseAt += args.PausedTime;
    }

    private void OnMobStateChanged(Entity<CrystalLanceComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            ClearCharge(ent);
    }

    private void OnCast(Entity<CrystalLanceComponent> ent, ref CrystalLanceEvent args)
    {
        var comp = ent.Comp;
        if (args.Handled || args.Action.Owner != comp.ActionEntity || !TerminatingOrDeleted(comp.ChargingLance) ||
            comp.ChargeTime <= TimeSpan.Zero || comp.ProjectileSpeed <= 0 || comp.ProjectileRange <= 0 ||
            !_mobState.IsAlive(ent.Owner) || _containers.IsEntityOrParentInContainer(ent.Owner) ||
            !args.Target.IsValid(EntityManager) ||
            !_actions.ValidateWorldTarget(ent.Owner, args.Target,
                (args.Action.Owner, Comp<WorldTargetActionComponent>(args.Action))))
            return;

        var origin = _transform.GetMapCoordinates(ent.Owner);
        var target = _transform.ToMapCoordinates(args.Target);
        if (origin.MapId != target.MapId || Vector2.DistanceSquared(origin.Position, target.Position) < 0.0001f)
            return;

        comp.Target = target;
        comp.ReleaseAt = _timing.CurTime + comp.ChargeTime;
        var charge = Spawn(comp.ChargePrototype, new EntityCoordinates(ent, Vector2.Zero));
        comp.ChargingLance = charge;
        EnsureComp<TimedDespawnComponent>(charge).Lifetime = (float) comp.ChargeTime.TotalSeconds + 1f;
        UpdateCharge(ent, charge, Vector2.Normalize(target.Position - origin.Position));
        _audio.PlayPvs(comp.ChargeSound, ent.Owner);
        _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(comp.Callout), InGameICChatType.Speak, ChatTransmitRange.Normal);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<CrystalLanceComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ChargingLance is not { } charge)
                continue;

            var origin = _transform.GetMapCoordinates(uid);
            var delta = comp.Target.Position - origin.Position;
            if (TerminatingOrDeleted(charge) || !_mobState.IsAlive(uid) ||
                _containers.IsEntityOrParentInContainer(uid) || origin.MapId != comp.Target.MapId ||
                delta.LengthSquared() < 0.0001f)
            {
                ClearCharge((uid, comp));
                continue;
            }

            var direction = Vector2.Normalize(delta);
            UpdateCharge((uid, comp), charge, direction);
            if (_timing.CurTime < comp.ReleaseAt)
                continue;

            var from = _transform.GetMapCoordinates(charge);
            // The growing visual may intersect cover or a close enemy; never skip over that obstacle.
            if (!_interaction.InRangeUnobstructed(origin, from, range: 0,
                    collisionMask: CollisionGroup.Impassable | CollisionGroup.BulletImpassable,
                    predicate: other => other == uid))
                from = origin;

            var projectile = Spawn(comp.ProjectilePrototype, from);
            EnsureComp<TimedDespawnComponent>(projectile).Lifetime = comp.ProjectileRange / comp.ProjectileSpeed;
            _scale.SetSpriteScale(projectile, comp.FullScale);
            _guns.ShootProjectile(projectile, direction, Vector2.Zero, uid, uid, comp.ProjectileSpeed);
            if (comp.ReleaseEffect is { } effect)
                Spawn(effect, from);
            _audio.PlayPvs(comp.ReleaseSound, uid);
            ClearCharge((uid, comp));
        }
    }

    private void UpdateCharge(Entity<CrystalLanceComponent> ent, EntityUid charge, Vector2 direction)
    {
        var comp = ent.Comp;
        var localOffset = (-_transform.GetWorldRotation(ent.Owner)).RotateVec(direction) * comp.ChargeOffset;
        _transform.SetCoordinates(charge, new EntityCoordinates(ent, localOffset));
        _transform.SetWorldRotation(charge, new Angle(direction));
        var progress = comp.ChargeTime <= TimeSpan.Zero ? 1f :
            Math.Clamp(1f - (float) ((comp.ReleaseAt - _timing.CurTime) / comp.ChargeTime), 0f, 1f);
        _scale.SetSpriteScale(charge, Vector2.Lerp(comp.InitialScale, comp.FullScale, progress));
    }

    private void ClearCharge(Entity<CrystalLanceComponent> ent)
    {
        QueueDel(ent.Comp.ChargingLance);
        ent.Comp.ChargingLance = null;
    }
}
