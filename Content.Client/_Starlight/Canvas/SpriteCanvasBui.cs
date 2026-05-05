using Content.Shared._Starlight.Canvas;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client._Starlight.Canvas;

[UsedImplicitly]
public sealed class SpriteCanvasBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private SpriteCanvasWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new SpriteCanvasWindow();
        _window.OpenCentered();

        _window.OnSaveRequested  += OnSaveRequested;
        _window.OnClearRequested += OnClearRequested;
        _window.OnSignRequested  += OnSignRequested;
        _window.OnClose          += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not SpriteCanvasBuiState s) return;

        // Determine if this local player has been granted override access.
        // IPlayerManager.LocalEntity gives the local character's EntityUid;
        // GetNetEntity converts it to the network ID used in the state set.
        var playerMgr = IoCManager.Resolve<IPlayerManager>();
        var localEnt  = playerMgr.LocalEntity;
        var canEdit   = !s.IsSigned
            || (localEnt.HasValue && s.OverrideGranted.Contains(EntMan.GetNetEntity(localEnt.Value)));

        _window?.Populate(s, canEdit);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _window?.Close();
        _window = null;
    }

    private void OnSaveRequested(List<CanvasStroke> newStrokes)
    {
        if (newStrokes.Count == 0) return;
        SendMessage(new SpriteCanvasAddStrokesMsg(newStrokes));
    }

    private void OnClearRequested()
    {
        SendMessage(new SpriteCanvasClearMsg());
    }

    private void OnSignRequested()
    {
        SendMessage(new SpriteCanvasSignMsg());
    }
}
