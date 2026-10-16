using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.TowerDefense;

/// <summary>Minigame health; intentionally independent of station combat and mob AI.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TowerDefenseEnemyComponent : Component
{
    [DataField, AutoNetworkedField] public float MaxHealth = 30;
    [DataField, AutoNetworkedField] public float Health = 30;
    [DataField, AutoNetworkedField] public bool Boss;
    [DataField] public float Speed = 1;
    [DataField] public int Reward = 10;
    [DataField] public int BaseDamage = 1;
}
