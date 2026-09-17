using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._Starlight.Dragon;

public sealed partial class WesternDragonCanDevourPrecondition : HTNPrecondition
{
    private WesternDragonBossSystem _boss = default!;
    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _boss = sysManager.GetEntitySystem<WesternDragonBossSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
        => _boss.CanConsiderDevour(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
}
