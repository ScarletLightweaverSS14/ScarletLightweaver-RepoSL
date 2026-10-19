using System.Linq;
using System.Numerics;
using Content.Client.ContextMenu.UI;
using Content.Client.Examine;
using Content.Client.Interaction;
using Content.Client.UserInterface.Screens;
using Content.Shared._Starlight.RTS;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using static Robust.Shared.Input.Binding.PointerInputCmdHandler;

namespace Content.Client._Starlight.RTS;

/// <summary>
/// Local selection and command controls for the dedicated RTS commander. Orders are validated by the server.
/// </summary>
public sealed partial class RtsCommanderSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<EntityUid> _selected = new();
    private EntityUid? _commander;
    private RtsCommandPanel? _panel;
    private Vector2 _panelAvailableSize;
    private MapCoordinates? _dragStart;
    private Vector2 _dragScreenStart;
    private EntityUid _clickedEntity;
    private bool _additive;
    private float _refreshTime;

    public IReadOnlyCollection<EntityUid> Selected => _selected;
    public MapCoordinates? OrderMarker { get; private set; }
    public bool Active => GetCommander() != null;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;

        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnSelect, false, true),
                typeof(DragDropSystem), typeof(SharedInteractionSystem))
            // Shift + left click is ExamineEntity in the standard SS14 bindings.
            .BindBefore(ContentKeyFunctions.ExamineEntity, new PointerInputCmdHandler(OnAddSelection, false, true),
                typeof(ExamineSystem))
            .BindBefore(EngineKeyFunctions.UseSecondary, new PointerInputCmdHandler(OnMove, outsidePrediction: true),
                typeof(EntityMenuUIController), typeof(SharedInteractionSystem))
            .Register<RtsCommanderSystem>();

        SubscribeNetworkEvent<RtsOrderFeedbackEvent>(OnFeedback);
        _overlays.AddOverlay(new RtsSelectionOverlay(this, EntityManager, _transform));
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<RtsCommanderSystem>();
        _overlays.RemoveOverlay<RtsSelectionOverlay>();
        _panel?.Orphan();
        _panel = null;
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var commander = GetCommander();
        var player = commander == null ? null : _player.LocalEntity;
        if (_commander != player)
        {
            _commander = player;
            _selected.Clear();
            _dragStart = null;
            OrderMarker = null;
            _panel?.Orphan();
            _panel = null;
        }

        if (commander == null)
            return;

        EnsurePanel();
        // A release over a UI control may not reach the world input handler.
        if (_dragStart != null && !_input.IsKeyDown(Keyboard.Key.MouseLeft))
            _dragStart = null;

        _refreshTime -= frameTime;
        if (_refreshTime > 0f)
            return;

        _refreshTime = 0.15f;
        _selected.RemoveWhere(uid => !CanSelect(uid, commander));
        RefreshPanel(commander);
    }

    private RtsCommanderComponent? GetCommander()
    {
        if (_player.LocalEntity is not { } uid ||
            !TryComp<RtsCommanderComponent>(uid, out var commander) ||
            !Exists(commander.Battlefield))
            return null;

        return commander;
    }

    private bool OnSelect(in PointerInputCmdArgs args) => Select(args, additive: false);

    private bool OnAddSelection(in PointerInputCmdArgs args) => Select(args, additive: true);

    private bool Select(in PointerInputCmdArgs args, bool additive)
    {
        if (GetCommander() is not { } commander)
            return false;

        var coordinates = _transform.ToMapCoordinates(args.Coordinates);
        if (!TryComp(commander.Battlefield, out TransformComponent? battlefield) ||
            coordinates.MapId != battlefield.MapID)
            return true;

        if (args.State == BoundKeyState.Down)
        {
            _dragStart = coordinates;
            _dragScreenStart = args.ScreenCoordinates.Position;
            _clickedEntity = args.EntityUid;
            _additive = additive;
            return true;
        }

        if (_dragStart is not { } start)
            return true;

        _dragStart = null;
        if (!_additive)
            _selected.Clear();

        if (Vector2.DistanceSquared(_dragScreenStart, args.ScreenCoordinates.Position) > 36f)
        {
            var box = SelectionBox(start.Position, coordinates.Position);
            var query = EntityQueryEnumerator<RtsUnitComponent, RtsOwnerComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var owner, out var xform))
            {
                if (owner.Team == commander.Team && xform.MapID == coordinates.MapId &&
                    box.Contains(_transform.GetWorldPosition(xform)))
                    AddSelection(uid);
            }
        }
        else
        {
            // Small unit sprites can be partly covered by scenery; allow a nearby friendly unit as a fallback.
            var clicked = CanSelect(_clickedEntity, commander)
                ? _clickedEntity
                : FindNearbyUnit(coordinates, commander);

            if (clicked is { } unit)
            {
                if (!_additive || !_selected.Remove(unit))
                    AddSelection(unit);
            }
        }

        RefreshPanel(commander);
        return true;
    }

    private EntityUid? FindNearbyUnit(MapCoordinates coordinates, RtsCommanderComponent commander)
    {
        EntityUid? nearest = null;
        var distance = 0.65f * 0.65f;
        var query = EntityQueryEnumerator<RtsUnitComponent, RtsOwnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var owner, out var xform))
        {
            if (owner.Team != commander.Team || xform.MapID != coordinates.MapId)
                continue;

            var candidate = Vector2.DistanceSquared(coordinates.Position, _transform.GetWorldPosition(xform));
            if (candidate >= distance)
                continue;

            distance = candidate;
            nearest = uid;
        }

        return nearest;
    }

    private bool CanSelect(EntityUid uid, RtsCommanderComponent commander)
    {
        return HasComp<RtsUnitComponent>(uid) &&
               TryComp<RtsOwnerComponent>(uid, out var owner) && owner.Team == commander.Team &&
               TryComp(uid, out TransformComponent? xform) &&
               TryComp(commander.Battlefield, out TransformComponent? battlefield) &&
               xform.MapID == battlefield.MapID;
    }

    private void AddSelection(EntityUid uid)
    {
        if (_selected.Count < RtsOrderRequestEvent.MaxUnits)
            _selected.Add(uid);
    }

    private bool OnMove(in PointerInputCmdArgs args)
    {
        if (GetCommander() is not { } commander)
            return false;

        _dragStart = null;
        var target = _transform.ToMapCoordinates(args.Coordinates);
        if (_selected.Count == 0)
        {
            _panel?.SetFeedback(Loc.GetString("rts-command-select-first"));
            return true;
        }

        if (!TryComp(commander.Battlefield, out TransformComponent? battlefield) ||
            battlefield.MapID != target.MapId)
            return true;

        var local = _transform.ToCoordinates(commander.Battlefield, target).Position;
        RaiseNetworkEvent(new RtsOrderRequestEvent(RtsOrderType.Move, SelectedNetEntities(), local));
        OrderMarker = target;
        return true;
    }

    private NetEntity[] SelectedNetEntities() => _selected.Select(uid => GetNetEntity(uid)).ToArray();

    private void Stop()
    {
        if (GetCommander() == null || _selected.Count == 0)
            return;

        RaiseNetworkEvent(new RtsOrderRequestEvent(RtsOrderType.Stop, SelectedNetEntities(), Vector2.Zero));
        OrderMarker = null;
    }

    private void SelectVisible(bool idleOnly = false)
    {
        if (GetCommander() is not { } commander ||
            !TryComp(commander.Battlefield, out TransformComponent? battlefield))
            return;

        _selected.Clear();
        var view = _eye.GetWorldViewbounds();
        var query = EntityQueryEnumerator<RtsUnitComponent, RtsOwnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var unit, out var owner, out var xform))
        {
            if (owner.Team == commander.Team && xform.MapID == battlefield.MapID &&
                view.Contains(_transform.GetWorldPosition(xform)) &&
                (!idleOnly || unit.State == RtsUnitState.Idle))
                AddSelection(uid);
        }

        RefreshPanel(commander);
        _panel?.SetFeedback(Loc.GetString(idleOnly
                ? (_selected.Count == 0 ? "rts-command-no-idle" : "rts-command-selected-idle")
                : "rts-command-selected-visible",
            ("count", _selected.Count)));
    }

    private void ClearSelection()
    {
        _selected.Clear();
        if (GetCommander() is { } commander)
            RefreshPanel(commander);
        _panel?.SetFeedback(Loc.GetString("rts-command-selection-cleared"));
    }

    private void OnFeedback(RtsOrderFeedbackEvent ev)
    {
        if (!Active)
            return;

        _panel?.SetFeedback(ev.Message);
        if (ev.Accepted == 0)
            OrderMarker = null;
    }

    private void EnsurePanel()
    {
        if (_panel == null)
        {
            _panel = new RtsCommandPanel();
            _panel.Stop.OnPressed += _ => Stop();
            _panel.SelectVisible.OnPressed += _ => SelectVisible();
            _panel.SelectIdle.OnPressed += _ => SelectVisible(idleOnly: true);
            _panel.Clear.OnPressed += _ => ClearSelection();
            _panel.Exit.OnPressed += _ => RaiseNetworkEvent(new RtsLeaveRequestEvent());
            LayoutContainer.SetAnchorPreset(_panel, LayoutContainer.LayoutPreset.BottomLeft);
            LayoutContainer.SetGrowHorizontal(_panel, LayoutContainer.GrowDirection.Constrain);
            LayoutContainer.SetGrowVertical(_panel, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetMarginLeft(_panel, 12);
            LayoutContainer.SetMarginTop(_panel, -80);
            LayoutContainer.SetMarginBottom(_panel, -80);
        }

        // StateRoot draws behind the gameplay screen. Add the HUD after the viewport,
        // and reparent it when switching between the normal and separated-chat screens.
        var parent = _ui.ActiveScreen?.FindControl<LayoutContainer>("ViewportContainer") ?? _ui.StateRoot;
        if (_panel.Parent != parent)
        {
            _panel.Orphan();
            parent.AddChild(_panel);
            _panelAvailableSize = Vector2.Zero;
        }

        var available = parent.Size;
        if (available.X <= 0 || available.Y <= 0)
            return;

        var width = MathF.Min(380, MathF.Max(0, available.X - 24));
        // The default chat shares the viewport; separated chat has its own reserved area.
        if (_ui.ActiveScreen is DefaultGameScreen screen && screen.ChatBox.VisibleInTree)
        {
            var chatLeft = screen.ChatBox.GlobalPosition.X - parent.GlobalPosition.X;
            width = MathF.Min(width, MathF.Max(0, chatLeft - 24));
        }

        var size = new Vector2(width, MathF.Max(0, available.Y - 128));
        if (size == _panelAvailableSize)
            return;
        _panelAvailableSize = size;
        _panel.SetWidth = width;
        _panel.MaxWidth = width;
        _panel.MaxHeight = size.Y;
        LayoutContainer.SetMarginRight(_panel, 12 + width);
    }

    private void RefreshPanel(RtsCommanderComponent commander)
    {
        if (_panel == null)
            return;

        var moving = 0;
        var blocked = 0;
        foreach (var uid in _selected)
        {
            if (!TryComp<RtsUnitComponent>(uid, out var unit))
                continue;

            if (unit.State == RtsUnitState.Moving)
                moving++;
            else if (unit.State == RtsUnitState.Blocked)
                blocked++;
        }

        var visible = 0;
        var view = _eye.GetWorldViewbounds();
        var battlefield = Transform(commander.Battlefield);
        var query = EntityQueryEnumerator<RtsUnitComponent, RtsOwnerComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var owner, out var xform))
        {
            if (owner.Team == commander.Team && xform.MapID == battlefield.MapID &&
                view.Contains(_transform.GetWorldPosition(xform)))
                visible++;
        }

        _panel.UpdateSelection(commander.Team, _selected.Count, moving, blocked, visible);
        if (TryComp(_commander, out TransformComponent? camera))
        {
            var position = _transform.ToCoordinates(commander.Battlefield, _transform.ToMapCoordinates(camera.Coordinates)).Position;
            _panel.SetBattlefieldInfo(Loc.GetString("rts-command-battlefield",
                ("size", commander.BattlefieldSize), ("x", (int) MathF.Floor(position.X)), ("y", (int) MathF.Floor(position.Y))));
        }
    }

    public Box2? GetDragBox(MapId map)
    {
        if (_dragStart is not { } start || start.MapId != map)
            return null;

        var cursor = _eye.ScreenToMap(_input.MouseScreenPosition);
        if (cursor.MapId != map ||
            Vector2.DistanceSquared(_dragScreenStart, _input.MouseScreenPosition.Position) <= 36f)
            return null;

        return SelectionBox(start.Position, cursor.Position);
    }

    private static Box2 SelectionBox(Vector2 start, Vector2 end) => new(Vector2.Min(start, end), Vector2.Max(start, end));
}
