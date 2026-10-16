using Content.Shared._Starlight.TowerDefense;
using Robust.Client.UserInterface;

namespace Content.Client._Starlight.TowerDefense;

public sealed class TowerDefenseBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private TowerDefenseWindow? _window;

    public TowerDefenseWindow? Window => _window;

    protected override void Open()
    {
        base.Open();

        // Initial replication and component startup can both queue Open for the same BUI.
        // Register its window and action handler only once for this BUI's lifetime.
        if (_window != null)
            return;

        _window = this.CreateWindow<TowerDefenseWindow>();
        _window.OnAction += message => SendMessage(message);
        if (State is TowerDefenseUiState state)
            _window.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TowerDefenseUiState arena)
            _window?.UpdateState(arena);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is TowerDefenseFeedbackMessage feedback)
            _window?.ShowFeedback(feedback.Message);
    }
}
