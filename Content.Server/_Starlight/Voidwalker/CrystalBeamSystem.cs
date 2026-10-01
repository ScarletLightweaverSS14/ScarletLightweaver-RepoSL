using System.Numerics;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Voidwalker;

public sealed partial class CrystalBeamSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private CrystalBeamTraceSystem _trace = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalBeamAbilityComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CrystalBeamAbilityComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CrystalBeamAbilityComponent, CrystalBeamEvent>(OnCast);
        SubscribeLocalEvent<CrystalBeamAbilityComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CrystalBeamAbilityComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<CrystalBeamComponent, ComponentShutdown>(OnChannelShutdown);
        SubscribeLocalEvent<CrystalBeamComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeNetworkEvent<CrystalBeamAimEvent>(OnAim);
    }

    private void OnStartup(Entity<CrystalBeamAbilityComponent> ent, ref ComponentStartup args)
        => _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);

    private void OnShutdown(Entity<CrystalBeamAbilityComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.Channel);
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnMobStateChanged(Entity<CrystalBeamAbilityComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            QueueDel(ent.Comp.Channel);
    }

    private void OnPlayerDetached(Entity<CrystalBeamAbilityComponent> ent, ref PlayerDetachedEvent args)
        => QueueDel(ent.Comp.Channel);

    private void OnUnpaused(Entity<CrystalBeamComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.StartedAt += args.PausedTime;
        ent.Comp.FireAt += args.PausedTime;
        ent.Comp.EndAt += args.PausedTime;
        if (TryComp<CrystalBeamDamageComponent>(ent, out var damage))
        {
            damage.NextPulse += args.PausedTime;
            damage.NextAim += args.PausedTime;
        }
        Dirty(ent);
    }

    private void OnChannelShutdown(Entity<CrystalBeamComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<CrystalBeamDamageComponent>(ent, out var damage))
            damage.Audio = _audio.Stop(damage.Audio);
        if (ent.Comp.Caster is not { } caster || !TryComp<CrystalBeamAbilityComponent>(caster, out var ability) ||
            ability.Channel != ent.Owner)
            return;
        ability.Channel = null;
        // Full recovery also applies to interrupted charges, preventing cancellation/recast exploits.
        if (_actions.GetAction(ability.ActionEntity) is { } action && action.Comp.UseDelay is { } cooldown)
            _actions.SetCooldown((action.Owner, action.Comp), cooldown);
    }

    private void OnCast(Entity<CrystalBeamAbilityComponent> ent, ref CrystalBeamEvent args)
    {
        if (args.Handled || args.Action.Owner != ent.Comp.ActionEntity || !TerminatingOrDeleted(ent.Comp.Channel) ||
            !_mobState.IsAlive(ent.Owner) || _containers.IsEntityOrParentInContainer(ent.Owner) ||
            !args.Target.IsValid(EntityManager) ||
            !_actions.ValidateWorldTarget(ent.Owner, args.Target,
                (args.Action.Owner, Comp<WorldTargetActionComponent>(args.Action))))
            return;

        var origin = _transform.GetMapCoordinates(ent.Owner);
        var target = _transform.ToMapCoordinates(args.Target);
        if (!ValidTarget(origin, target))
            return;

        var channel = Spawn(ent.Comp.ChannelPrototype, new EntityCoordinates(ent, Vector2.Zero));
        var beam = Comp<CrystalBeamComponent>(channel);
        var damage = Comp<CrystalBeamDamageComponent>(channel);
        if (beam.ChargeTime <= TimeSpan.Zero || beam.Duration <= TimeSpan.Zero || beam.PulseInterval <= TimeSpan.Zero ||
            !float.IsFinite(beam.Range) || beam.Range <= 0 || !float.IsFinite(beam.Width) || beam.Width <= 0 ||
            !double.IsFinite(beam.RotationSpeed) || beam.RotationSpeed <= 0)
        {
            QueueDel(channel);
            return;
        }

        ent.Comp.Channel = channel;
        beam.Caster = ent.Owner;
        beam.Direction = new Angle(target.Position - origin.Position);
        beam.StartedAt = _timing.CurTime;
        beam.FireAt = beam.StartedAt + beam.ChargeTime;
        beam.EndAt = beam.FireAt + beam.Duration;
        damage.NextPulse = beam.FireAt;
        damage.Target = target;
        EnsureComp<TimedDespawnComponent>(channel).Lifetime = (float) (beam.ChargeTime + beam.Duration).TotalSeconds + 1;
        damage.Audio = _audio.PlayPvs(beam.ChargeSound, ent.Owner)?.Entity;
        _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(beam.Callout), InGameICChatType.Speak, ChatTransmitRange.Normal);
        Dirty(channel, beam);
        args.Cooldown = beam.ChargeTime + beam.Duration + (args.Action.Comp.UseDelay ?? TimeSpan.Zero);
        args.Handled = true;
    }

    private static bool ValidTarget(MapCoordinates origin, MapCoordinates target)
        => origin.MapId != MapId.Nullspace && origin.MapId == target.MapId &&
           float.IsFinite(target.X) && float.IsFinite(target.Y) &&
           Vector2.DistanceSquared(origin.Position, target.Position) > 0.0001f;

    private void OnAim(CrystalBeamAimEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is { } caster)
            TrySetAim(caster, GetEntity(args.Channel), args.Target);
    }

    /// <summary>Validates ownership and rate-limits input; clients never supply the resulting beam direction or hits.</summary>
    public bool TrySetAim(EntityUid caster, EntityUid channel, MapCoordinates target)
    {
        if (TerminatingOrDeleted(channel) || !TryComp<CrystalBeamComponent>(channel, out var beam) ||
            beam.Caster != caster || !TryComp<CrystalBeamAbilityComponent>(caster, out var ability) ||
            ability.Channel != channel || !TryComp<CrystalBeamDamageComponent>(channel, out var damage) ||
            _timing.CurTime < damage.NextAim || _timing.CurTime >= beam.EndAt ||
            !ValidTarget(_transform.GetMapCoordinates(caster), target))
            return false;

        damage.Target = target;
        damage.NextAim = _timing.CurTime + beam.AimInterval;
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<CrystalBeamComponent, CrystalBeamDamageComponent>();
        while (query.MoveNext(out var uid, out var beam, out var damage))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            if (beam.Caster is not { } caster || TerminatingOrDeleted(caster) || !_mobState.IsAlive(caster) ||
                !TryComp<CrystalBeamAbilityComponent>(caster, out var ability) || ability.Channel != uid ||
                _containers.IsEntityOrParentInContainer(caster) || _timing.CurTime >= beam.EndAt)
            {
                QueueDel(uid);
                continue;
            }

            var origin = _transform.GetMapCoordinates(caster);
            if (origin.MapId != damage.Target.MapId || origin.MapId == MapId.Nullspace)
            {
                QueueDel(uid);
                continue;
            }

            var delta = damage.Target.Position - origin.Position;
            if (delta.LengthSquared() > 0.0001f)
            {
                var turn = Angle.ShortestDistance(beam.Direction, new Angle(delta)).Theta;
                var limit = MathHelper.DegreesToRadians(beam.RotationSpeed) * frameTime;
                beam.Direction = (beam.Direction + new Angle(Math.Clamp(turn, -limit, limit))).Reduced();
            }

            if (_timing.CurTime < beam.FireAt)
            {
                Dirty(uid, beam);
                continue;
            }
            if (!beam.Firing)
            {
                beam.Firing = true;
                _audio.Stop(damage.Audio);
                damage.Audio = _audio.PlayPvs(beam.SustainSound, caster)?.Entity;
            }

            beam.Length = _trace.Trace(beam, origin, beam.Range, damage.Targets);
            Dirty(uid, beam);
            if (_timing.CurTime < damage.NextPulse)
                continue;

            foreach (var (target, distance) in damage.Targets)
            {
                if (distance <= beam.Length + 0.001f && !TerminatingOrDeleted(target))
                    _damage.TryChangeDamage(target, damage.Damage, origin: caster);
            }
            damage.NextPulse += beam.PulseInterval;
            // Drop missed pulses after a late tick; never apply a backlog of damage in one frame.
            if (damage.NextPulse <= _timing.CurTime)
                damage.NextPulse = _timing.CurTime + beam.PulseInterval;
        }
    }
}
