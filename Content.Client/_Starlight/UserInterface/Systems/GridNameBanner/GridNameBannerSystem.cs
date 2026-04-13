using Content.Client.GameTicking.Managers;
using Content.Client.Gameplay;
using Content.Shared._Starlight.Time;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Maths;

namespace Content.Client._Starlight.UserInterface.Systems.GridNameBanner;

/// <summary>
///     Shows a typewriter-effect location banner whenever the local player
///     spawns or steps from one grid onto another.
/// </summary>
[UsedImplicitly]
public sealed class GridNameBannerSystem : UIController,
    IOnStateEntered<GameplayState>,
    IOnStateExited<GameplayState>
{
    [Dependency] private readonly IPlayerManager       _player     = default!;

    [UISystemDependency] private readonly ClientGameTicker _gameTicker = default!;

    private GridNameBannerControl? _control;

    /// <summary>Whether the station banner has already been shown this gameplay session.</summary>
    private bool _stationShown;

    /// <summary>Last grid the local player was on. Used to detect grid transitions.</summary>
    private EntityUid? _lastGrid;

    // ── UIController lifecycle ────────────────────────────────────────────────

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
    }

    public void OnStateEntered(GameplayState state)
    {
        _control      = new GridNameBannerControl();
        _stationShown = false;

        // Add to WindowRoot so it renders above the game HUD.
        // Position at 1/3 from the left so the banner sits inside the game viewport.
        UIManager.WindowRoot.AddChild(_control);
        LayoutContainer.SetAnchorLeft(_control,   0.33f);
        LayoutContainer.SetAnchorRight(_control,  0.33f);
        LayoutContainer.SetAnchorTop(_control,    0f);
        LayoutContainer.SetAnchorBottom(_control, 0f);
        LayoutContainer.SetMarginTop(_control,    70f);
    }

    public void OnStateExited(GameplayState state)
    {
        if (_control != null)
        {
            UIManager.WindowRoot.RemoveChild(_control);
            _control.Dispose();
            _control  = null;
        }
        _lastGrid     = null;
        _stationShown = false;
    }

    // ── Per-frame grid polling ────────────────────────────────────────────────

    public override void FrameUpdate(FrameEventArgs args)
    {
        if (_control == null || _player.LocalEntity is not { } playerEnt)
            return;

        if (!EntityManager.TryGetComponent<TransformComponent>(playerEnt, out var xform))
            return;

        var currentGrid = xform.GridUid;
        if (currentGrid == _lastGrid)
            return;

        _lastGrid = currentGrid;

        // Only show the banner when landing ON a grid (not when entering nullspace).
        if (currentGrid != null)
            ShowGridBanner(currentGrid.Value);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        if (_control == null)
            return;

        // Record the initial grid so FrameUpdate does not fire a second banner
        // for the same grid immediately after the station banner is shown.
        if (EntityManager.TryGetComponent<TransformComponent>(args.Entity, out var xform))
            _lastGrid = xform.GridUid;

        ShowStationBanner();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _lastGrid = null;
    }

    // ── Banner helpers ────────────────────────────────────────────────────────

    /// <summary>
    ///     Full station card: station name, IC date, and IC time.
    ///     Shown only once per gameplay session (first spawn).
    /// </summary>
    private void ShowStationBanner()
    {
        if (_control == null || _stationShown)
            return;

        // Pick the first available station name; fall back to a generic title.
        var stationName = "Unknown Station";
        if (_gameTicker != null)
        {
            foreach (var name in _gameTicker.StationNames.Values)
            {
                stationName = name;
                break;
            }
        }

        // Lazy-resolve TimeSystem so we don't depend on injection timing.
        var timeSystem = EntitySystemManager.GetEntitySystemOrNull<TimeSystem>();
        string text;
        if (timeSystem != null)
        {
            var (time, date) = timeSystem.GetStationTime();
            var timeStr      = $"{time.Hours:D2}:{time.Minutes:D2}";
            text = $"{stationName}\n{date}\n{timeStr}";
        }
        else
        {
            text = stationName;
        }

        _stationShown = true;
        _control.Show(text);
    }

    /// <summary>
    ///     Simple grid name card shown when stepping onto a new grid mid-round.
    /// </summary>
    private void ShowGridBanner(EntityUid gridUid)
    {
        if (_control == null)
            return;

        if (!EntityManager.TryGetComponent<MetaDataComponent>(gridUid, out var meta))
            return;

        _control.Show(meta.EntityName);
    }
}
