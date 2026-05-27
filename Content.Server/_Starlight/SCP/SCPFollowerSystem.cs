using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Starlight.SCP;
using Robust.Shared.Map;
using System.Numerics;

namespace Content.Server._Starlight.SCP;

/// <summary>
/// Keeps the HTN <c>FollowTarget</c> blackboard entry updated every tick
/// so the NPC continuously chases its assigned player entity.
/// </summary>
public sealed class SCPFollowerSystem : EntitySystem
{
    [Dependency] private readonly NPCSystem _npc = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SCPFollowerComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var follower, out _))
        {
            if (follower.Target is not { } targetUid)
                continue;
            if (!EntityManager.EntityExists(targetUid))
                continue;

            // Re-push every tick so the NPC reacts immediately if the player moves.
            _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget,
                new EntityCoordinates(targetUid, Vector2.Zero));
        }
    }

    /// <summary>
    /// Assigns a follow target to the SCP entity and primes the HTN blackboard.
    /// </summary>
    public void SetFollowTarget(EntityUid scpUid, EntityUid target)
    {
        var comp = EnsureComp<SCPFollowerComponent>(scpUid);
        comp.Target = target;
        _npc.SetBlackboard(scpUid, NPCBlackboard.FollowTarget,
            new EntityCoordinates(target, Vector2.Zero));
    }
}
