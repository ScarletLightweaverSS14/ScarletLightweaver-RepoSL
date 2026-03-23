using Content.Client._Starlight.Weapons.Gunnery;
using Content.Shared._Starlight.Weapons.Gunnery;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._Starlight.Weapons.Gunnery;

[UsedImplicitly]
public sealed class GunneryConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;

    private GunneryConsoleWindow? _window;
    private bool _lastIncomingMissile;

    public GunneryConsoleBoundUserInterface(EntityUid owner, Enum uiKey)
        : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<GunneryConsoleWindow>();

        _window.OnFireRequested = (cannon, target) =>
        {
            // Convert EntityCoordinates → NetCoordinates for the network message.
            SendPredictedMessage(new GunneryConsoleFireMessage
            {
                Cannon = cannon,
                Target = EntMan.GetNetCoordinates(target),
            });
        };

        _window.OnGuidanceUpdate = target =>
        {
            // Guidance messages are not predicted — they steer a physics body server-side.
            SendMessage(new GunneryConsoleGuidanceMessage
            {
                Target = EntMan.GetNetCoordinates(target),
            });
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not GunneryConsoleBoundUserInterfaceState cState)
            return;

        // Play missile lock alarm when the flag first becomes true.
        if (cState.IncomingMissile && !_lastIncomingMissile)
        {
            var audio = _sysMan.GetEntitySystem<SharedAudioSystem>();
            audio.PlayGlobal(
                audio.ResolveSound(new SoundPathSpecifier("/Audio/Effects/Shuttle/radar_ping.ogg")),
                Filter.Local(), false,
                AudioParams.Default.WithVolume(4f));
        }
        _lastIncomingMissile = cState.IncomingMissile;

        _window?.UpdateState(cState);
    }
}
