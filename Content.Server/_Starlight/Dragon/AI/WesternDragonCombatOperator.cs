using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._Starlight.Dragon;

/// <summary>The HTN owns the combat lifecycle; ordinary steering owns actual movement.</summary>
public sealed partial class WesternDragonCombatOperator : HTNOperator
{
    private WesternDragonBossSystem _boss = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _boss = sysManager.GetEntitySystem<WesternDragonBossSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _boss.Tick(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), blackboard)
            ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
        => _boss.Stop(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), blackboard);

    public override void PlanShutdown(NPCBlackboard blackboard)
        => _boss.Stop(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), blackboard);
}
