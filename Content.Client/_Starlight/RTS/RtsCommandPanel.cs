using Content.Shared._Starlight.RTS;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Starlight.RTS;

/// <summary>
/// Compact field-command controls. Text wraps and the contents scroll on smaller HUD layouts.
/// </summary>
public sealed class RtsCommandPanel : PanelContainer
{
    public readonly Button Stop;
    public readonly Button SelectVisible;
    public readonly Button SelectIdle;
    public readonly Button Clear;
    public readonly Button Exit;
    public readonly Button Help;
    private readonly Label _heading;
    private readonly Label _battlefield;
    private readonly Label _selection;
    private readonly Label _visible;
    private readonly Label _idle;
    private readonly Label _moving;
    private readonly Label _blocked;
    private readonly RichTextLabel _feedback;
    private readonly GridContainer _actions;
    private readonly BoxContainer _selectionRow;
    private readonly BoxContainer _states;

    public RtsCommandPanel()
    {
        MouseFilter = MouseFilterMode.Stop;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#17212BED"),
            BorderColor = Color.FromHex("#4D8799"),
            BorderThickness = new Thickness(1, 3, 1, 1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
        };

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            ReturnMeasure = true,
            HorizontalExpand = true,
            VerticalExpand = true,
            MaxHeight = 300,
        };
        scroll.AddChild(content);
        AddChild(scroll);

        _heading = new Label { FontColorOverride = Color.FromHex("#98DFF3"), ClipText = true };
        content.AddChild(_heading);
        _battlefield = new Label
        {
            FontColorOverride = Color.LightGray,
            ClipText = true,
            Visible = false,
        };
        content.AddChild(_battlefield);

        _selectionRow = Row();
        _selection = new Label { HorizontalExpand = true, ClipText = true };
        _visible = new Label { FontColorOverride = Color.LightGray, HorizontalExpand = true, ClipText = true };
        _selectionRow.AddChild(_selection);
        _selectionRow.AddChild(_visible);
        content.AddChild(_selectionRow);

        _states = Row();
        _idle = StateLabel("#B8C7D1");
        _moving = StateLabel("#9AE2BB");
        _blocked = StateLabel("#F4C77A");
        _states.AddChild(_idle);
        _states.AddChild(_moving);
        _states.AddChild(_blocked);
        content.AddChild(_states);

        var hint = new RichTextLabel { HorizontalExpand = true };
        hint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("rts-command-quick-help")), Color.LightGray);
        content.AddChild(hint);

        _actions = new GridContainer
        {
            Columns = 3,
            HorizontalExpand = true,
        };
        Stop = ActionButton("rts-command-stop", "rts-command-stop-tooltip");
        SelectVisible = ActionButton("rts-command-select-visible", "rts-command-select-visible-tooltip");
        SelectIdle = ActionButton("rts-command-select-idle", "rts-command-select-idle-tooltip");
        _actions.AddChild(Stop);
        _actions.AddChild(SelectVisible);
        _actions.AddChild(SelectIdle);

        Clear = ActionButton("rts-command-clear", "rts-command-clear-tooltip");
        Help = ActionButton("rts-command-controls", "rts-command-controls-tooltip");
        Help.ToggleMode = true;
        Exit = ActionButton("rts-command-exit", "rts-command-exit-tooltip");
        _actions.AddChild(Clear);
        _actions.AddChild(Help);
        _actions.AddChild(Exit);
        content.AddChild(_actions);

        var feedbackPanel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1820"),
                ContentMarginLeftOverride = 7,
                ContentMarginRightOverride = 7,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5,
            },
        };
        _feedback = new RichTextLabel { HorizontalExpand = true, MinHeight = 30 };
        feedbackPanel.AddChild(_feedback);
        content.AddChild(feedbackPanel);
        SetFeedback(Loc.GetString("rts-command-ready"));

        var helpText = new RichTextLabel
        {
            HorizontalExpand = true,
            Visible = false,
        };
        // The span params overload emits IL rejected by the content sandbox.
        helpText.SetMessage(FormattedMessage.FromUnformatted(string.Join("\n", new[]
        {
            Loc.GetString("rts-command-help-select"),
            Loc.GetString("rts-command-help-move"),
            Loc.GetString("rts-command-help-camera"),
            Loc.GetString("rts-command-help-blocked"),
        })), Color.LightGray);
        Help.OnToggled += args => helpText.Visible = args.Pressed;
        content.AddChild(helpText);

        UpdateSelection(RtsTeam.Blue, 0, 0, 0, 0);
    }

    public void UpdateSelection(RtsTeam team, int count, int moving, int blocked, int visible)
    {
        _heading.Text = Loc.GetString(team == RtsTeam.Blue ? "rts-command-blue" : "rts-command-red");
        _heading.ToolTip = _heading.Text;
        _heading.FontColorOverride = team == RtsTeam.Blue ? Color.FromHex("#98DFF3") : Color.FromHex("#F0A0A0");
        _selection.Text = Loc.GetString("rts-command-selection", ("count", count), ("limit", RtsOrderRequestEvent.MaxUnits));
        _visible.Text = Loc.GetString("rts-command-visible", ("count", visible));
        _visible.ToolTip = Loc.GetString("rts-command-visible-tooltip");
        _idle.Text = Loc.GetString("rts-command-idle", ("count", Math.Max(0, count - moving - blocked)));
        _moving.Text = Loc.GetString("rts-command-moving", ("count", moving));
        _blocked.Text = Loc.GetString("rts-command-blocked", ("count", blocked));
        _blocked.ToolTip = Loc.GetString("rts-command-help-blocked");
        Stop.Disabled = count == 0;
        Clear.Disabled = count == 0;
        SelectVisible.Disabled = visible == 0;
        SelectIdle.Disabled = visible == 0;
    }

    public void SetBattlefieldInfo(string message)
    {
        _battlefield.Text = message;
        _battlefield.ToolTip = message;
        _battlefield.Visible = !string.IsNullOrWhiteSpace(message);
    }

    public void SetFeedback(string message)
    {
        _feedback.SetMessage(FormattedMessage.FromUnformatted(message), Color.FromHex("#D4EAF0"));
        _feedback.ToolTip = message;
    }

    protected override void Resized()
    {
        base.Resized();
        var columns = Width < 260 ? 1 : Width < 340 ? 2 : 3;
        if (_actions.Columns != columns)
            _actions.Columns = columns;
        var orientation = Width < 260 ? BoxContainer.LayoutOrientation.Vertical : BoxContainer.LayoutOrientation.Horizontal;
        if (_selectionRow.Orientation != orientation)
        {
            _selectionRow.Orientation = orientation;
            _states.Orientation = orientation;
        }
    }

    private static BoxContainer Row()
    {
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
    }

    private static Label StateLabel(string color)
    {
        return new Label { FontColorOverride = Color.FromHex(color), HorizontalExpand = true, ClipText = true };
    }

    private static Button ActionButton(string text, string tooltip)
    {
        return new Button
        {
            Text = Loc.GetString(text),
            ToolTip = Loc.GetString(tooltip),
            HorizontalExpand = true,
            ClipText = true,
        };
    }
}
