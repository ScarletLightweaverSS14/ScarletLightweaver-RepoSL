using System.Numerics;
using Content.Shared._Starlight.TowerDefense;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Starlight.TowerDefense;

public sealed class TowerDefenseWindow : DefaultWindow
{
    private readonly Label _status = new() { Name = "TowerStatus" };
    private readonly Label _selection = new() { Name = "TowerSelection" };
    private readonly Label _feedback = new() { Name = "TowerFeedback", Visible = false };
    private readonly BoxContainer _shop = new();
    private readonly Button _upgrade = new() { Name = "TowerUpgrade", Visible = false };
    private readonly Button _start = new() { Name = "TowerStart" };
    private readonly Button _reset = new() { Name = "TowerReset" };
    private readonly GridContainer _board = new();
    private readonly Dictionary<Vector2i, Button> _buttons = new();
    private readonly Dictionary<string, Button> _shopButtons = new();
    private TowerDefenseUiState? _state;
    private string? _placing;
    private Vector2i? _selected;

    public event Action<TowerDefenseMessage>? OnAction;

    public TowerDefenseWindow()
    {
        Title = Loc.GetString("td-title");
        MinSize = new Vector2(560, 520);
        SetSize = new Vector2(640, 620);
        var layout = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 5,
        };
        // Keep the complete board and controls accessible on smaller displays.
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(layout);
        Contents.AddChild(scroll);
        layout.AddChild(_status);
        layout.AddChild(new Label { Text = Loc.GetString("td-help") });
        layout.AddChild(_shop);
        layout.AddChild(_board);
        layout.AddChild(_selection);
        layout.AddChild(_upgrade);
        layout.AddChild(_feedback);

        _upgrade.OnPressed += _ => UpgradeSelected();
        _start.Text = Loc.GetString("td-start");
        _start.OnPressed += _ => OnAction?.Invoke(new(TowerDefenseAction.Start));
        _reset.Text = Loc.GetString("td-reset");
        _reset.OnPressed += _ =>
        {
            ClearSelection();
            RefreshSelection();
            OnAction?.Invoke(new(TowerDefenseAction.Reset));
        };
        var actions = new BoxContainer { HorizontalExpand = true };
        _start.HorizontalExpand = true;
        _reset.HorizontalExpand = true;
        actions.AddChild(_start);
        actions.AddChild(_reset);
        layout.AddChild(actions);
    }

    public void UpdateState(TowerDefenseUiState state)
    {
        var rebuildBoard = _state == null || _state.Width != state.Width || _state.Height != state.Height;
        var reset = _state != null && state.Phase == TowerDefensePhase.Ready &&
                    (_state.Phase is TowerDefensePhase.Victory or TowerDefensePhase.Defeat ||
                     state.Towers.Count == 0 && _state.Towers.Count != 0);
        _state = state;
        if (reset || rebuildBoard)
            ClearSelection();

        if (rebuildBoard)
        {
            _board.RemoveAllChildren();
            _buttons.Clear();
            _board.Columns = state.Width;
            // North is up, matching grid coordinates in the world.
            for (var y = state.Height - 1; y >= 0; y--)
            for (var x = 0; x < state.Width; x++)
            {
                var tile = new Vector2i(x, y);
                var button = new Button { Name = $"TowerTile_{x}_{y}", MinSize = new Vector2(30, 30) };
                button.OnPressed += _ => ClickTile(tile);
                _board.AddChild(button);
                _buttons.Add(tile, button);
            }
        }

        // Cash and enemy updates reuse the existing shop and board controls.
        var rebuildShop = _shopButtons.Count != state.Catalog.Count;
        foreach (var tower in state.Catalog)
            rebuildShop |= !_shopButtons.ContainsKey(tower.Id);
        if (rebuildShop)
        {
            _shop.RemoveAllChildren();
            _shopButtons.Clear();
            foreach (var tower in state.Catalog)
            {
                var id = tower.Id;
                var button = new Button { Name = $"TowerShop_{id}", HorizontalExpand = true, ToggleMode = true };
                button.OnPressed += _ => SelectPurchase(id);
                _shopButtons.Add(id, button);
                _shop.AddChild(button);
            }
        }

        var ended = IsEnded(state);
        if (_placing != null && (ended || GetPurchase(_placing) is not { } purchase || state.Cash < purchase.Cost))
            _placing = null;
        if (_selected is { } selected && !state.Towers.ContainsKey(selected))
            _selected = null;

        _status.Text = Loc.GetString("td-status", ("cash", state.Cash), ("hp", state.BaseHealth),
            ("wave", state.Wave), ("total", state.TotalWaves), ("alive", state.Alive),
            ("remaining", state.Remaining),
            ("phase", Loc.GetString($"td-phase-{state.Phase.ToString().ToLowerInvariant()}")));
        _start.Disabled = state.Phase != TowerDefensePhase.Ready;
        _reset.Disabled = !ended;
        RefreshSelection();
    }

    public void ShowFeedback(string message)
    {
        _feedback.Text = Loc.GetString(message);
        _feedback.Visible = true;
    }

    private static bool IsEnded(TowerDefenseUiState state)
        => state.Phase is TowerDefensePhase.Victory or TowerDefensePhase.Defeat;

    private TowerDefenseTowerUiData? GetPurchase(string id)
    {
        if (_state == null)
            return null;
        foreach (var tower in _state.Catalog)
        {
            if (tower.Id == id)
                return tower;
        }
        return null;
    }

    private void SelectPurchase(string id)
    {
        if (_state == null || IsEnded(_state) || GetPurchase(id) is not { } tower || _state.Cash < tower.Cost)
            return;
        _placing = _placing == id ? null : id;
        _selected = null;
        _feedback.Visible = false;
        RefreshSelection();
    }

    private void ClickTile(Vector2i tile)
    {
        if (_state == null || _state.Road.Contains(tile))
            return;
        _feedback.Visible = false;
        if (_state.Towers.ContainsKey(tile))
        {
            _selected = tile;
            _placing = null;
        }
        else if (_placing != null && !IsEnded(_state))
        {
            OnAction?.Invoke(new(TowerDefenseAction.Place, tile, _placing));
        }
        else
        {
            _selected = null;
        }
        RefreshSelection();
    }

    private void UpgradeSelected()
    {
        if (_state == null || IsEnded(_state) || _selected is not { } tile ||
            !_state.Towers.TryGetValue(tile, out var tower) || tower.UpgradeCost is not { } cost ||
            _state.Cash < cost)
            return;
        _feedback.Visible = false;
        OnAction?.Invoke(new(TowerDefenseAction.Upgrade, tile, tower.Id, tower.Level));
    }

    private void ClearSelection()
    {
        _selected = null;
        _placing = null;
        _feedback.Text = string.Empty;
        _feedback.Visible = false;
    }

    private void RefreshSelection()
    {
        if (_state == null)
            return;

        foreach (var tower in _state.Catalog)
        {
            var button = _shopButtons[tower.Id];
            button.Pressed = _placing == tower.Id;
            button.Text = Loc.GetString(button.Pressed ? "td-buy-cancel" : "td-buy",
                ("name", Loc.GetString(tower.Name)), ("cost", tower.Cost));
            button.Disabled = IsEnded(_state) || _state.Cash < tower.Cost;
        }

        TowerDefenseTowerUiData? inspected = null;
        _upgrade.Visible = false;
        if (_placing != null && GetPurchase(_placing) is { } purchase)
        {
            _selection.Text = Loc.GetString("td-place", ("name", Loc.GetString(purchase.Name)),
                ("cost", purchase.Cost)) + "\n" + DescribeTower(purchase);
        }
        else if (_selected is { } selected && _state.Towers.TryGetValue(selected, out inspected))
        {
            _selection.Text = DescribeTower(inspected) + "\n" + Loc.GetString("td-range-help");
            _upgrade.Visible = true;
            _upgrade.Text = inspected.UpgradeCost is { } cost
                ? Loc.GetString("td-upgrade", ("level", inspected.Level + 1), ("cost", cost))
                : Loc.GetString("td-max-level");
            _upgrade.Disabled = IsEnded(_state) || inspected.UpgradeCost == null ||
                                _state.Cash < inspected.UpgradeCost.Value;
        }
        else
        {
            _selection.Text = Loc.GetString("td-select");
        }

        foreach (var (tile, button) in _buttons)
        {
            var road = _state.Road.Contains(tile);
            var occupied = _state.Towers.TryGetValue(tile, out var tower);
            button.Text = occupied ? $"{tower!.Symbol}{tower.Level}" : tile == _state.Start ? ">" : tile == _state.End ? "B" : road ? "#" : ".";
            button.Disabled = road || IsEnded(_state) && !occupied;
            button.ToolTip = occupied ? DescribeTower(tower!) : null;
            var inRange = _selected is { } center && inspected != null &&
                          (tile - center).LengthSquared <= inspected.Range * inspected.Range;
            button.ModulateSelfOverride = _selected == tile ? Color.FromHex("#70DBFF") :
                tile == _state.Start ? Color.FromHex("#A6E6A6") :
                tile == _state.End ? Color.FromHex("#F0B07D") :
                inRange ? Color.FromHex("#87B7E6") :
                road ? Color.FromHex("#AAAAAA") :
                _placing != null && !occupied ? Color.FromHex("#A6D69C") : null;
        }
    }

    private static string DescribeTower(TowerDefenseTowerUiData tower)
        => Loc.GetString("td-inspect", ("name", Loc.GetString(tower.Name)), ("level", tower.Level),
            ("max", tower.MaxLevel), ("weapon", Loc.GetString(tower.LevelName)),
            ("damage", tower.Damage), ("range", tower.Range), ("cooldown", tower.Cooldown));
}
