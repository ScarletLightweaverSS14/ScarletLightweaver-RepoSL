using Content.Shared._Starlight.AlloySmeltery;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Starlight.AlloySmeltery.UI;

[UsedImplicitly]
public sealed class AlloySmelteryBoundUserInterface : BoundUserInterface
{
    private AlloySmelteryWindow? _window;

    public AlloySmelteryBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<AlloySmelteryWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not AlloySmelteryBuiState cast)
            return;

        _window.UpdateState(cast);
    }
}
