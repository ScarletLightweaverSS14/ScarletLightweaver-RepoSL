using System.Numerics;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Charges.Systems;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Voidwalker;

public sealed partial class CrystalVolleySystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedChargesSystem _charges = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalVolleyComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CrystalVolleyComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CrystalVolleyComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<CrystalVolleyComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CrystalVolleyComponent, SummonCrystalVolleyEvent>(OnSummon);
        SubscribeLocalEvent<CrystalVolleyComponent, FireCrystalVolleyEvent>(OnFire);
    }

    private void OnStartup(Entity<CrystalVolleyComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.SummonActionEntity, ent.Comp.SummonAction);
    }

    private void OnShutdown(Entity<CrystalVolleyComponent> ent, ref ComponentShutdown args)
    {
        ClearVolley(ent);
        _actions.RemoveAction(ent.Owner, ent.Comp.SummonActionEntity);
        QueueDel(ent.Comp.SummonActionEntity);
        ent.Comp.SummonActionEntity = null;
    }

    private void OnUnpaused(Entity<CrystalVolleyComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.ExpiresAt += args.PausedTime;
    }

    private void OnMobStateChanged(Entity<CrystalVolleyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            ClearVolley(ent);
    }

    private void OnSummon(Entity<CrystalVolleyComponent> ent, ref SummonCrystalVolleyEvent args)
    {
        if (args.Handled || args.Action.Owner != ent.Comp.SummonActionEntity ||
            ent.Comp.Crystals.Count != 0 || ent.Comp.CrystalCount <= 0 ||
            ent.Comp.VolleyLifetime <= TimeSpan.Zero || _containers.IsEntityInContainer(ent.Owner))
            return;

        var comp = ent.Comp;
        if (!_actions.AddAction(ent, ref comp.FireActionEntity, comp.FireAction))
            return;

        comp.OrbitPhase = 0;
        comp.ExpiresAt = _timing.CurTime + comp.VolleyLifetime;
        for (var i = 0; i < comp.CrystalCount; i++)
        {
            var crystal = Spawn(comp.HoverPrototype, new EntityCoordinates(ent, Vector2.Zero));
            // A failsafe also cleans up detached crystals if their owner disappears.
            EnsureComp<TimedDespawnComponent>(crystal).Lifetime = (float) comp.VolleyLifetime.TotalSeconds;
            comp.Crystals.Add(crystal);
        }

        UpdateFormation(ent);
        _charges.SetMaxCharges(comp.FireActionEntity.Value, comp.CrystalCount);
        _charges.SetCharges(comp.FireActionEntity.Value, comp.CrystalCount);
        _actions.SetEnabled(comp.SummonActionEntity, false);

        if (comp.SummonEffect is { } effect)
            Spawn(effect, Transform(ent).Coordinates);

        _audio.PlayPvs(comp.SummonSound, ent.Owner);
        _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(comp.Callout), InGameICChatType.Speak, ChatTransmitRange.Normal);
        _popup.PopupEntity(Loc.GetString("voidwalker-crystal-volley-ready"), ent.Owner, ent.Owner);
        args.Handled = true;
    }

    private void OnFire(Entity<CrystalVolleyComponent> ent, ref FireCrystalVolleyEvent args)
    {
        var comp = ent.Comp;
        if (args.Handled || args.Action.Owner != comp.FireActionEntity ||
            _timing.CurTime >= comp.ExpiresAt || _containers.IsEntityInContainer(ent.Owner))
            return;

        var index = comp.Crystals.FindIndex(crystal => !TerminatingOrDeleted(crystal));
        if (index < 0)
            return;

        var ownerCoords = _transform.GetMapCoordinates(ent.Owner);
        // Snapshot this click in map space. No cursor/target entity is stored on the projectile.
        var target = _transform.ToMapCoordinates(args.Target);
        if (ownerCoords.MapId != target.MapId ||
            Vector2.DistanceSquared(ownerCoords.Position, target.Position) < 0.0001f)
            return;

        var crystal = comp.Crystals[index];
        var from = _transform.GetMapCoordinates(crystal);
        // Visuals may orbit into a wall. Never use that to spawn a projectile on its far side.
        if (!_interaction.InRangeUnobstructed(ent.Owner, Transform(crystal).Coordinates,
                range: comp.OrbitRadius + comp.BobHeight + 0.5f))
            from = ownerCoords;

        var direction = target.Position - from.Position;
        if (direction.LengthSquared() < 0.0001f)
            return;

        var projectile = Spawn(comp.ProjectilePrototype, from);
        // Deliberately omit caster velocity so movement cannot skew the clicked trajectory.
        _guns.ShootProjectile(projectile, direction, Vector2.Zero, ent.Owner, ent.Owner, comp.ProjectileSpeed);
        QueueDel(crystal);
        comp.Crystals[index] = EntityUid.Invalid;
        _audio.PlayPvs(comp.FireSound, ent.Owner);
        args.Handled = true;

        if (comp.Crystals.TrueForAll(uid => TerminatingOrDeleted(uid)))
            ClearVolley(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<CrystalVolleyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Crystals.Count == 0)
                continue;

            if (_timing.CurTime >= comp.ExpiresAt || _containers.IsEntityInContainer(uid) ||
                comp.Crystals.TrueForAll(crystal => TerminatingOrDeleted(crystal)))
            {
                ClearVolley((uid, comp));
                continue;
            }

            comp.OrbitPhase += frameTime;
            UpdateFormation((uid, comp));
        }
    }

    private void UpdateFormation(Entity<CrystalVolleyComponent> ent)
    {
        var comp = ent.Comp;
        for (var i = 0; i < comp.Crystals.Count; i++)
        {
            var crystal = comp.Crystals[i];
            if (TerminatingOrDeleted(crystal))
                continue;

            var phase = MathF.Tau * i / comp.Crystals.Count;
            var angle = new Angle(phase + comp.OrbitPhase * comp.OrbitSpeed);
            var offset = angle.ToVec() * comp.OrbitRadius;
            offset.Y += MathF.Sin(comp.OrbitPhase * comp.BobFrequency + phase) * comp.BobHeight;
            _transform.SetCoordinates(crystal, new EntityCoordinates(ent, offset));
            _transform.SetLocalRotation(crystal, angle);
        }
    }

    private void ClearVolley(Entity<CrystalVolleyComponent> ent)
    {
        foreach (var crystal in ent.Comp.Crystals)
        {
            if (!TerminatingOrDeleted(crystal))
                QueueDel(crystal);
        }

        ent.Comp.Crystals.Clear();
        _actions.RemoveAction(ent.Owner, ent.Comp.FireActionEntity);
        ent.Comp.FireActionEntity = null;
        _actions.SetEnabled(ent.Comp.SummonActionEntity, true);
    }
}
