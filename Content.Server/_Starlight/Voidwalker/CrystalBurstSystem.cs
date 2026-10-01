using System.Numerics;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Voidwalker;

public sealed partial class CrystalBurstSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalBurstComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CrystalBurstComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CrystalBurstComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<CrystalBurstComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CrystalBurstComponent, CrystalBurstEvent>(OnCast);
    }

    private void OnStartup(Entity<CrystalBurstComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<CrystalBurstComponent> ent, ref ComponentShutdown args)
    {
        ClearFormation(ent);
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnUnpaused(Entity<CrystalBurstComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.LaunchAt += args.PausedTime;
    }

    private void OnMobStateChanged(Entity<CrystalBurstComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            ClearFormation(ent);
    }

    private void OnCast(Entity<CrystalBurstComponent> ent, ref CrystalBurstEvent args)
    {
        var comp = ent.Comp;
        if (args.Handled || args.Action.Owner != comp.ActionEntity || comp.Crystals.Count > 0 ||
            comp.CrystalCount <= 0 || comp.WarningDuration <= TimeSpan.Zero ||
            comp.ProjectileSpeed <= 0 || comp.ProjectileRange <= 0 ||
            comp.InitialRadius < 0 || comp.FormationRadius < comp.InitialRadius ||
            !_mobState.IsAlive(ent.Owner) || _containers.IsEntityOrParentInContainer(ent.Owner))
            return;

        comp.LaunchAt = _timing.CurTime + comp.WarningDuration;
        for (var i = 0; i < comp.CrystalCount; i++)
        {
            var crystal = Spawn(comp.HoverPrototype, new EntityCoordinates(ent, Vector2.Zero));
            // Detached warning visuals must still expire if the cast is interrupted.
            EnsureComp<TimedDespawnComponent>(crystal).Lifetime = (float) comp.WarningDuration.TotalSeconds + 1f;
            comp.Crystals.Add(crystal);
        }

        UpdateFormation(ent);
        _audio.PlayPvs(comp.FormationSound, ent.Owner);
        _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(comp.Callout), InGameICChatType.Speak, ChatTransmitRange.Normal);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<CrystalBurstComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Crystals.Count == 0)
                continue;

            if (!_mobState.IsAlive(uid) || _containers.IsEntityOrParentInContainer(uid))
            {
                ClearFormation((uid, comp));
                continue;
            }

            UpdateFormation((uid, comp));
            if (_timing.CurTime >= comp.LaunchAt)
                Launch((uid, comp));
        }
    }

    private void UpdateFormation(Entity<CrystalBurstComponent> ent)
    {
        var comp = ent.Comp;
        var progress = comp.WarningDuration <= TimeSpan.Zero ? 1f :
            Math.Clamp(1f - (float) ((comp.LaunchAt - _timing.CurTime) / comp.WarningDuration), 0f, 1f);
        var radius = comp.InitialRadius + (comp.FormationRadius - comp.InitialRadius) * progress;
        for (var i = 0; i < comp.Crystals.Count; i++)
        {
            var crystal = comp.Crystals[i];
            if (TerminatingOrDeleted(crystal))
                continue;

            var angle = new Angle(MathF.Tau * i / comp.Crystals.Count);
            _transform.SetCoordinates(crystal, new EntityCoordinates(ent, angle.ToVec() * radius));
            _transform.SetLocalRotation(crystal, angle);
        }
    }

    private void Launch(Entity<CrystalBurstComponent> ent)
    {
        var comp = ent.Comp;
        var origin = _transform.GetMapCoordinates(ent.Owner);
        foreach (var crystal in comp.Crystals)
        {
            if (TerminatingOrDeleted(crystal))
                continue;

            var from = _transform.GetMapCoordinates(crystal);
            var direction = _transform.GetWorldRotation(crystal).ToVec();
            // Check projectile blockers, including nearby mobs, so the ring cannot skip over them.
            if (!_interaction.InRangeUnobstructed(origin, from, range: 0,
                    collisionMask: CollisionGroup.Impassable | CollisionGroup.BulletImpassable,
                    predicate: uid => uid == ent.Owner))
                from = origin;

            var projectile = Spawn(comp.ProjectilePrototype, from);
            // Physics can move a newly spawned shard before its first lifetime decrement,
            // and once more while TimedDespawn's queued deletion is pending. Budget both
            // steps so a fast, short-lived shard cannot gain extra range from tick ordering.
            var physicsStep = (float) _timing.TickPeriod.TotalSeconds;
            EnsureComp<TimedDespawnComponent>(projectile).Lifetime =
                MathF.Max(0, comp.ProjectileRange / comp.ProjectileSpeed - 2 * physicsStep);
            // Directions are captured at launch; caster movement/turning cannot steer fired crystals.
            _guns.ShootProjectile(projectile, direction, Vector2.Zero, ent.Owner, ent.Owner, comp.ProjectileSpeed);
        }

        if (comp.BurstEffect is { } effect)
            Spawn(effect, origin);
        _audio.PlayPvs(comp.BurstSound, ent.Owner);
        ClearFormation(ent);
    }

    private void ClearFormation(Entity<CrystalBurstComponent> ent)
    {
        foreach (var crystal in ent.Comp.Crystals)
        {
            if (!TerminatingOrDeleted(crystal))
                QueueDel(crystal);
        }
        ent.Comp.Crystals.Clear();
    }
}
