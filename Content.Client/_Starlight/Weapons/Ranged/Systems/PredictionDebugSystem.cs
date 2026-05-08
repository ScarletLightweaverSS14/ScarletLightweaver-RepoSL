using Content.Client._Starlight.Weapons.Ranged.Overlays;
using Content.Client.Weapons.Ranged.Systems;
using Content.Shared.Starlight.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._Starlight.Weapons.Ranged.Systems;

/// <summary>
/// Manages the <see cref="PredictionDebugOverlay"/>.
/// Enable via:  gun.prediction_debug true
/// Simulate lag via:  net.fakelagmin 0.1   net.fakelagrand 0.05
/// </summary>
public sealed class PredictionDebugSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan   = default!;
    [Dependency] private readonly IConfigurationManager _cfg    = default!;
    [Dependency] private readonly IEyeManager _eye              = default!;
    [Dependency] private readonly IUserInterfaceManager _ui     = default!;
    [Dependency] private readonly IPlayerManager _player        = default!;
    [Dependency] private readonly IClientNetManager _net        = default!;
    [Dependency] private readonly IResourceCache _res           = default!;
    [Dependency] private readonly IGameTiming _timing           = default!;

    private PredictionDebugOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, StarlightCCVars.PredictionDebugOverlay, OnToggle, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        RemoveOverlay();
    }

    private void OnToggle(bool enabled)
    {
        if (enabled)
            AddOverlay();
        else
            RemoveOverlay();
    }

    private void AddOverlay()
    {
        if (_overlay != null)
            return;

        var gunSystem = EntityManager.System<GunSystem>();
        _overlay = new PredictionDebugOverlay(EntityManager, _eye, _ui, _player, _net, _cfg, _timing, gunSystem, _res);
        _overlayMan.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlayMan.RemoveOverlay(_overlay);
        _overlay = null;
    }
}
