using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.Components;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Random;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
/// Operator that makes hostile HUMANOID mobs call for help from nearby allies.
/// Only works for entities with HumanoidAppearanceComponent (e.g., syndies, nukies, pirates).
/// </summary>
public sealed partial class CallForHelpOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    
    private ChatSystem _chat = default!;
    private NpcFactionSystem _faction = default!;
    private EntityLookupSystem _lookup = default!;

    /// <summary>
    /// Key where the threat entity is stored
    /// </summary>
    [DataField("threatKey")]
    public string ThreatKey = "Target";

    /// <summary>
    /// Range to search for allies
    /// </summary>
    [DataField("allySearchRadius")]
    public float AllySearchRadius = 20f;

    /// <summary>
    /// Cooldown between help calls (stored in blackboard)
    /// </summary>
    [DataField("helpCallCooldown")]
    public float HelpCallCooldown = 30f;

    /// <summary>
    /// Key to store when the last help call was made
    /// </summary>
    [DataField("lastHelpCallKey")]
    public string LastHelpCallKey = "LastHelpCallTime";

    /// <summary>
    /// Key to store if allies responded
    /// </summary>
    [DataField("alliesRespondedKey")]
    public string AlliesRespondedKey = "AlliesResponded";

    /// <summary>
    /// Possible help phrases
    /// </summary>
    private static readonly string[] HelpPhrases = new[]
    {
        "Help!",
        "I need backup!",
        "Under attack!",
        "Help me!",
        "Assistance needed!",
    };

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _chat = sysManager.GetEntitySystem<ChatSystem>();
        _faction = sysManager.GetEntitySystem<NpcFactionSystem>();
        _lookup = sysManager.GetEntitySystem<EntityLookupSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Check if entity still exists
        if (!_entManager.EntityExists(owner))
            return (false, null);

        // Only humanoids can call for help (syndies, nukies, pirates, etc.)
        if (!_entManager.HasComponent<HumanoidAppearanceComponent>(owner))
            return (false, null);

        // Check if on cooldown
        if (blackboard.TryGetValue<float>(LastHelpCallKey, out var lastCallTime, _entManager))
        {
            var currentTime = (float)_entManager.System<GameTicker>().RoundDuration().TotalSeconds;
            if (currentTime - lastCallTime < HelpCallCooldown)
            {
                return (false, null); // Still on cooldown
            }
        }

        // Must have a threat to call for help about
        if (!blackboard.TryGetValue<EntityUid>(ThreatKey, out var threat, _entManager))
            return (false, null);

        // Check if threat still exists
        if (!_entManager.EntityExists(threat))
            return (false, null);

        // Check if there are nearby allies
        if (!_entManager.TryGetComponent<NpcFactionMemberComponent>(owner, out var faction))
            return (false, null);

        var xform = _entManager.GetComponent<TransformComponent>(owner);
        var allies = _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(xform.Coordinates, AllySearchRadius)
            .Where(e => e.Owner != owner && _faction.IsEntityFriendly((owner, faction), (e.Owner, null)))
            .ToList();

        if (allies.Count == 0)
            return (false, null); // No allies nearby

        return (true, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Check if entity still exists
        if (!_entManager.EntityExists(owner))
            return;

        // Emit help call
        var helpPhrase = _random.Pick(HelpPhrases);
        _chat.TrySendInGameICMessage(owner, helpPhrase, InGameICChatType.Speak, ChatTransmitRange.Normal);

        // Update cooldown
        var currentTime = (float)_entManager.System<GameTicker>().RoundDuration().TotalSeconds;
        blackboard.SetValue(LastHelpCallKey, currentTime);

        // Alert nearby allies by raising an event they can listen to
        if (blackboard.TryGetValue<EntityUid>(ThreatKey, out var threat, _entManager))
        {
            // Check if threat still exists
            if (!_entManager.EntityExists(threat))
                return;

            var xform = _entManager.GetComponent<TransformComponent>(owner);
            
            if (!_entManager.TryGetComponent<TransformComponent>(threat, out var threatXform))
                return;
            
            // Find nearby hostile NPCs that are allies
            if (_entManager.TryGetComponent<NpcFactionMemberComponent>(owner, out var faction))
            {
                var allies = _lookup.GetEntitiesInRange<HTNComponent>(xform.Coordinates, AllySearchRadius)
                    .Where(e => e.Owner != owner && _faction.IsEntityFriendly((owner, faction), (e.Owner, null)))
                    .ToList();

                var alliesResponded = false;
                
                // Alert each ally about the threat
                foreach (var ally in allies)
                {
                    // Check if ally still exists
                    if (!_entManager.EntityExists(ally.Owner))
                        continue;

                    if (_entManager.TryGetComponent<HTNComponent>(ally.Owner, out var htn))
                    {
                        // Set the threat as the ally's target if they don't have one
                        if (!htn.Blackboard.ContainsKey(ThreatKey))
                        {
                            htn.Blackboard.SetValue(ThreatKey, threat);
                            htn.Blackboard.SetValue("TargetCoordinates", threatXform.Coordinates);
                            alliesResponded = true;
                        }
                    }
                }

                // Store whether allies responded
                blackboard.SetValue(AlliesRespondedKey, alliesResponded);
            }
        }
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        // This is an instant action
        return HTNOperatorStatus.Finished;
    }
}
