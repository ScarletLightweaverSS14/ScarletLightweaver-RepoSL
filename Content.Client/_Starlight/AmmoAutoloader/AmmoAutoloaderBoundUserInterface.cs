using Content.Client._Starlight.AmmoAutoloader.UI;
using Content.Shared._Starlight.AmmoAutoloader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Starlight.AmmoAutoloader;

public sealed class AmmoAutoloaderBoundUserInterface : BoundUserInterface
{
    private AmmoAutoloaderWindow? _window;

    public AmmoAutoloaderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<AmmoAutoloaderWindow>();

        _window.ToggleSide1 += () => SendMessage(new AmmoAutoloaderToggleSide1Message());
        _window.ToggleSide2 += () => SendMessage(new AmmoAutoloaderToggleSide2Message());
        _window.EjectMag1   += () => SendMessage(new AmmoAutoloaderEjectMag1Message());
        _window.EjectMag2   += () => SendMessage(new AmmoAutoloaderEjectMag2Message());
        _window.EjectAmmo1  += () => SendMessage(new AmmoAutoloaderEjectAmmo1Message());
        _window.EjectAmmo2  += () => SendMessage(new AmmoAutoloaderEjectAmmo2Message());

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is AmmoAutoloaderBuiState s)
            _window?.UpdateState(s);
    }
}
