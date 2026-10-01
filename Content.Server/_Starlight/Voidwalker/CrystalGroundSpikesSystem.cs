using System.Numerics;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Voidwalker;

public sealed partial class CrystalGroundSpikesSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalGroundSpikesComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CrystalGroundSpikesComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CrystalGroundSpikesComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CrystalGroundSpikesComponent, CrystalGroundSpikesEvent>(OnCast);
        SubscribeLocalEvent<CrystalFaultLineComponent, EntityUnpausedEvent>(OnUnpaused);
    }

    private void OnStartup(Entity<CrystalGroundSpikesComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<CrystalGroundSpikesComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.ActiveFaultLine);
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
    }

    private void OnMobStateChanged(Entity<CrystalGroundSpikesComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            QueueDel(ent.Comp.ActiveFaultLine);
    }

    private void OnUnpaused(Entity<CrystalFaultLineComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.NextEruption += args.PausedTime;
    }

    private void OnCast(Entity<CrystalGroundSpikesComponent> ent, ref CrystalGroundSpikesEvent args)
    {
        if (args.Handled || args.Action.Owner != ent.Comp.ActionEntity ||
            !TerminatingOrDeleted(ent.Comp.ActiveFaultLine) || !_mobState.IsAlive(ent.Owner) ||
            _containers.IsEntityOrParentInContainer(ent.Owner) || !args.Target.IsValid(EntityManager) ||
            !_actions.ValidateWorldTarget(ent.Owner, args.Target,
                (args.Action.Owner, Comp<WorldTargetActionComponent>(args.Action))))
            return;

        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp) ||
            !_map.TryGetTileRef(grid, gridComp, xform.Coordinates, out var tile) || _turf.IsSpace(tile))
        {
            ShowBlocked(ent);
            return;
        }

        var origin = _transform.WithEntityId(xform.Coordinates, grid);
        var target = _transform.WithEntityId(args.Target, grid);
        var direction = target.Position - origin.Position;
        if (direction.LengthSquared() < 0.0001f)
            return;

        var wave = Spawn(ent.Comp.FaultLinePrototype, origin);
        var fault = Comp<CrystalFaultLineComponent>(wave);
        fault.Caster = ent.Owner;
        fault.Direction = Vector2.Normalize(direction);
        fault.NextEruption = _timing.CurTime + fault.InitialDelay;
        if (fault.SpikeCount <= 0 || fault.FirstSpikeDistance <= 0 || fault.SpikeSpacing <= 0 ||
            !CanErupt((wave, fault), fault.Direction * fault.FirstSpikeDistance))
        {
            QueueDel(wave);
            ShowBlocked(ent);
            return;
        }

        ent.Comp.ActiveFaultLine = wave;
        if (ent.Comp.StompEffect is { } effect)
            Spawn(effect, origin);
        _audio.PlayPvs(ent.Comp.StompSound, ent.Owner);
        _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(ent.Comp.Callout),
            InGameICChatType.Speak, ChatTransmitRange.Normal);
        args.Handled = true;
    }

    private void ShowBlocked(EntityUid caster)
    {
        _popup.PopupEntity(Loc.GetString("voidwalker-ground-spikes-blocked"), caster, caster);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<CrystalFaultLineComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Caster is not { } caster || TerminatingOrDeleted(caster) ||
                !HasComp<CrystalGroundSpikesComponent>(caster) || !_mobState.IsAlive(caster) ||
                _containers.IsEntityOrParentInContainer(caster) ||
                Transform(caster).MapID != Transform(uid).MapID || comp.NextSpike >= comp.SpikeCount)
            {
                QueueDel(uid);
                continue;
            }

            if (_timing.CurTime < comp.NextEruption)
                continue;

            var offset = comp.Direction * (comp.FirstSpikeDistance + comp.NextSpike * comp.SpikeSpacing);
            if (!CanErupt((uid, comp), offset))
            {
                QueueDel(uid);
                continue;
            }

            // Keep spawned spikes on the grid, not parented to the short-lived wave controller.
            var coords = Transform(uid).Coordinates.Offset(offset);
            var spike = Spawn(comp.SpikePrototype, coords);
            if (comp.EruptionEffect is { } effect)
                Spawn(effect, coords);
            _audio.PlayPvs(comp.EruptionSound, spike);
            HitTargets((uid, comp), coords, caster);

            comp.PreviousOffset = offset;
            comp.NextSpike++;
            // At most one eruption per update: a late tick must not collapse the travelling wave.
            comp.NextEruption = _timing.CurTime + comp.EruptionDelay;
            if (comp.NextSpike >= comp.SpikeCount)
                QueueDel(uid);
        }
    }

    private bool CanErupt(Entity<CrystalFaultLineComponent> ent, Vector2 offset)
    {
        var origin = Transform(ent).Coordinates;
        if (!TryComp<MapGridComponent>(origin.EntityId, out var grid))
            return false;

        // Sample the whole segment so diagonal lines and wider spacing cannot jump floor gaps.
        var segment = offset - ent.Comp.PreviousOffset;
        var steps = Math.Max(1, (int) MathF.Ceiling(segment.Length() / (grid.TileSize / 4f)));
        for (var i = 0; i <= steps; i++)
        {
            var sample = origin.Offset(ent.Comp.PreviousOffset + segment * (i / (float) steps));
            if (!_map.TryGetTileRef(origin.EntityId, grid, sample, out var tile) || _turf.IsSpace(tile))
                return false;
        }

        return _interaction.InRangeUnobstructed(
            _transform.ToMapCoordinates(origin.Offset(ent.Comp.PreviousOffset)),
            _transform.ToMapCoordinates(origin.Offset(offset)),
            range: 0, collisionMask: ent.Comp.ObstructionMask);
    }

    private void HitTargets(Entity<CrystalFaultLineComponent> ent, EntityCoordinates coords, EntityUid caster)
    {
        var comp = ent.Comp;
        var from = _transform.ToMapCoordinates(coords);
        foreach (var target in _lookup.GetEntitiesInRange(coords, comp.HitRadius, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (target == caster || !HasComp<MobStateComponent>(target) || !HasComp<DamageableComponent>(target) ||
                (comp.HitOncePerCast && comp.HitEntities.Contains(target)) ||
                _containers.IsEntityOrParentInContainer(target) ||
                !_interaction.InRangeUnobstructed(from, _transform.GetMapCoordinates(target), range: 0,
                    collisionMask: comp.ObstructionMask, predicate: uid => uid == target || uid == caster))
                continue;

            comp.HitEntities.Add(target);
            _damage.TryChangeDamage(target, comp.Damage, origin: caster);
            _stamina.TakeStaminaDamage(target, comp.StaminaDamage, source: caster, with: ent.Owner);
        }
    }
}
