using Content.Client.Lathe;
using Content.Client.Materials;
using Content.Shared._Starlight.AmmoAutoloader;
using Content.Shared.Power;
using Robust.Client.GameObjects;

namespace Content.Client._Starlight.AmmoAutoloader;

public sealed partial class AmmoAutoloaderClientSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AmmoAutoloaderComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, AmmoAutoloaderComponent comp, ref AppearanceChangeEvent args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        _appearance.TryGetData<bool>(uid, AmmoAutoloaderVisuals.IsJammed,  out var jammed,  args.Component);
        _appearance.TryGetData<bool>(uid, AmmoAutoloaderVisuals.IsRunning, out var running, args.Component);
        _appearance.TryGetData<bool>(uid, PowerDeviceVisuals.Powered,      out var powered, args.Component);

        // ── Base layer (icon / loading) ───────────────────────────────────────────
        if (sprite.LayerMapTryGet(LatheVisualLayers.IsRunning, out _))
        {
            var state = (jammed || !running) ? "icon" : "loading";
            sprite.LayerSetState(LatheVisualLayers.IsRunning, state);
        }

        // ── Unlit layer ───────────────────────────────────────────────────────────
        if (sprite.LayerMapTryGet(PowerDeviceVisuals.Powered, out _))
        {
            sprite.LayerSetVisible(PowerDeviceVisuals.Powered, powered);
            sprite.LayerSetState(PowerDeviceVisuals.Powered, "unlit");
        }

        // ── Inserting overlay (animates when loading) ─────────────────────────────
        if (sprite.LayerMapTryGet(MaterialStorageVisualLayers.Inserting, out _))
        {
            sprite.LayerSetVisible(MaterialStorageVisualLayers.Inserting, running && !jammed);
            if (running && !jammed)
                sprite.LayerSetAnimationTime(MaterialStorageVisualLayers.Inserting, 0f);
        }
    }
}
