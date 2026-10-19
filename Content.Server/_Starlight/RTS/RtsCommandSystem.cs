using System.Numerics;
using Content.Server._Starlight.RTS.Components;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Shared._Starlight.RTS;
using Content.Shared.Ghost;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.RTS;

/// <summary>Session authorization, squad orders, and commander-camera control.</summary>
public sealed partial class RtsCommandSystem : EntitySystem
{
    [Dependency] private RtsUnitSystem _units = default!;
    [Dependency] private MindSystem _minds = default!;
    [Dependency] private GhostSystem _ghosts = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RtsOrderRequestEvent>(OnOrder);
        SubscribeNetworkEvent<RtsLeaveRequestEvent>(OnLeave);
        SubscribeLocalEvent<RtsCommanderComponent, PlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<RtsCommanderSessionComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<RtsCommanderComponent, InteractionAttemptEvent>(OnInteraction);
    }

    private void OnInteraction(Entity<RtsCommanderComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    /// <summary>Controls a camera while preserving any surviving body for the player's return.</summary>
    public EntityUid Join(ICommonSession session, Entity<RtsBattlefieldComponent> battlefield, RtsTeam team)
    {
        var mind = _minds.GetOrCreateMind(session.UserId);
        var returnCoordinates = EntityCoordinates.Invalid;
        if (session.AttachedEntity is { } previous)
        {
            returnCoordinates = TryComp<RtsCommanderSessionComponent>(previous, out var previousVisit)
                ? previousVisit.ReturnCoordinates
                : Transform(previous).Coordinates;
        }

        if (mind.Comp.VisitingEntity != null)
            _minds.UnVisit(mind, mind.Comp);

        var headquarters = team == RtsTeam.Blue
            ? battlefield.Comp.BlueHeadquarters
            : battlefield.Comp.RedHeadquarters;
        var position = TryComp(headquarters, out TransformComponent? headquartersTransform)
            ? headquartersTransform.LocalPosition + new Vector2(team == RtsTeam.Blue ? 4 : -4, 0)
            : new Vector2(battlefield.Comp.Size / 2f);
        var camera = Spawn("RtsCommander", new EntityCoordinates(battlefield, position));
        var commander = Comp<RtsCommanderComponent>(camera);
        commander.Battlefield = battlefield;
        commander.BattlefieldSize = battlefield.Comp.Size;
        commander.Team = team;
        Dirty(camera, commander);
        var visit = AddComp<RtsCommanderSessionComponent>(camera);
        visit.Mind = mind;
        visit.ReturnCoordinates = returnCoordinates;
        // Detached observers delete themselves. Visiting from an owned observer lets that
        // deletion promote the visit through TransferTo, which finds the same visiting
        // mind on the camera and ghosts it out. Transfer ownership before cleanup instead.
        if (mind.Comp.OwnedEntity is not { } owned || TerminatingOrDeleted(owned) ||
            HasComp<GhostComponent>(owned) || HasComp<RtsCommanderComponent>(owned))
            _minds.TransferTo(mind, camera, mind: mind.Comp);
        else
            _minds.Visit(mind, camera, mind.Comp);
        return camera;
    }

    public void Leave(EntityUid commander)
    {
        if (!HasComp<RtsCommanderComponent>(commander))
            return;

        RestoreMind(commander);
        QueueDel(commander);
    }

    private void OnDetached(Entity<RtsCommanderComponent> ent, ref PlayerDetachedEvent args)
    {
        QueueDel(ent);
    }

    private void OnTerminating(Entity<RtsCommanderSessionComponent> ent, ref EntityTerminatingEvent args)
    {
        RestoreMind(ent);
    }

    private void RestoreMind(EntityUid commander)
    {
        if (!TryComp<RtsCommanderSessionComponent>(commander, out var visit) ||
            !TryComp<MindComponent>(visit.Mind, out var mind))
            return;

        if (mind.VisitingEntity == commander)
            _minds.UnVisit(visit.Mind, mind);

        // Ordinary observer ghosts delete themselves when detached. Recreate an observer if
        // there is no original body left; SpawnGhost handles a deleted return map as well.
        if (mind.CurrentEntity == null || mind.CurrentEntity == commander || TerminatingOrDeleted(mind.CurrentEntity.Value))
            _ghosts.SpawnGhost((visit.Mind, mind), visit.ReturnCoordinates);
    }

    private void OnLeave(RtsLeaveRequestEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } commander)
            Leave(commander);
    }

    private void OnOrder(RtsOrderRequestEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (session.AttachedEntity is not { } commander ||
            !HasComp<RtsCommanderComponent>(commander) || msg.Units == null ||
            msg.Units.Length is 0 or > RtsOrderRequestEvent.MaxUnits)
            return;

        // Stop must always interrupt the previous move immediately. Only moves start
        // pathfinding work and need this short request cooldown.
        if (msg.Order == RtsOrderType.Move)
        {
            var state = EnsureComp<RtsCommanderSessionComponent>(commander);
            if (state.NextOrder > _timing.CurTime)
                return;
            state.NextOrder = _timing.CurTime + TimeSpan.FromMilliseconds(150);
        }

        var units = new List<EntityUid>(msg.Units.Length);
        foreach (var netUnit in msg.Units)
        {
            if (TryGetEntity(netUnit, out var unit))
                units.Add(unit.Value);
        }

        var accepted = IssueOrder(commander, msg.Order, units, msg.Target);
        var message = accepted == 0
            ? Loc.GetString("rts-order-rejected")
            : Loc.GetString(msg.Order == RtsOrderType.Move ? "rts-order-moving" : "rts-order-stopped", ("count", accepted));
        RaiseNetworkEvent(new RtsOrderFeedbackEvent(accepted, message), session);
    }

    /// <summary>Validates an order against server-owned commander and unit data.</summary>
    public int IssueOrder(EntityUid commander, RtsOrderType order, IReadOnlyList<EntityUid> requested, Vector2 target)
    {
        if (TerminatingOrDeleted(commander) || order is not (RtsOrderType.Move or RtsOrderType.Stop) ||
            requested.Count is 0 or > RtsOrderRequestEvent.MaxUnits ||
            !TryComp<RtsCommanderComponent>(commander, out var actor) ||
            TerminatingOrDeleted(actor.Battlefield) ||
            !TryComp<RtsBattlefieldComponent>(actor.Battlefield, out var battlefield) ||
            Transform(commander).MapUid != actor.Battlefield)
            return 0;

        if (order == RtsOrderType.Move &&
            (!float.IsFinite(target.X) || !float.IsFinite(target.Y) ||
             target.X < RtsUnitSystem.BattlefieldMargin || target.Y < RtsUnitSystem.BattlefieldMargin ||
             target.X > battlefield.Size - RtsUnitSystem.BattlefieldMargin ||
             target.Y > battlefield.Size - RtsUnitSystem.BattlefieldMargin))
            return 0;

        var seen = new HashSet<EntityUid>();
        var units = new List<EntityUid>();
        foreach (var uid in requested)
        {
            if (!seen.Add(uid) || TerminatingOrDeleted(uid) ||
                !HasComp<RtsUnitComponent>(uid) ||
                !TryComp<RtsOwnerComponent>(uid, out var owner) || owner.Team != actor.Team ||
                Transform(uid).MapUid != actor.Battlefield)
                continue;
            units.Add(uid);
        }

        var columns = (int) MathF.Ceiling(MathF.Sqrt(units.Count));
        var rows = columns == 0 ? 0 : (units.Count + columns - 1) / columns;
        var halfSize = new Vector2((columns - 1) * 0.5f, (rows - 1) * 0.5f);
        var center = Vector2.Clamp(target, new Vector2(0.5f) + halfSize,
            new Vector2(battlefield.Size - 0.5f) - halfSize);
        var accepted = 0;
        for (var i = 0; i < units.Count; i++)
        {
            if (order == RtsOrderType.Stop)
            {
                _units.Stop(units[i]);
                accepted++;
                continue;
            }

            var offset = new Vector2(i % columns, i / columns) - halfSize;
            if (_units.IssueMove(units[i], new EntityCoordinates(actor.Battlefield, center + offset)))
                accepted++;
        }

        return accepted;
    }
}
