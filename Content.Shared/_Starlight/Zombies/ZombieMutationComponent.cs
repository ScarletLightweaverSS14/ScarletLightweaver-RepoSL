using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Zombies;

/// <summary>
/// Tracks zombie mutation state for entities that have recently been zombified.
/// After 5–10 minutes the zombie unlocks a one-time mutation ability.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ZombieMutationComponent : Component
{
    /// <summary>
    /// The game time at which the mutation ability becomes available.
    /// Set during zombification based on a random 5–10 minute window.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan MutationUnlockTime = TimeSpan.Zero;

    /// <summary>
    /// Whether the player has already completed their mutation.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool HasMutated = false;

    /// <summary>
    /// Whether the mutation action has been granted to the player yet
    /// (to avoid spamming the popup/action).
    /// </summary>
    [DataField]
    public bool MutationReady = false;

    /// <summary>
    /// Reference to the spawned action entity, so it can be removed after mutation.
    /// </summary>
    [DataField]
    public EntityUid? MutationActionEntity = null;

    /// <summary>
    /// The action prototype to grant when the mutation unlocks.
    /// </summary>
    [DataField]
    public EntProtoId MutationAction = "ActionZombieEvolve";
}
