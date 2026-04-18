using Content.Client.Eui;
using Content.Shared._Starlight.Administration;
using Content.Shared.Eui;

namespace Content.Client._Starlight.Administration.UI;

/// <summary>
/// Client-side EUI for the Clear Lag admin tool.
/// </summary>
public sealed class ClearLagEui : BaseEui
{
    private readonly ClearLagWindow _window;

    public ClearLagEui()
    {
        _window = new ClearLagWindow();
        _window.OnClose  += () => SendMessage(new CloseEuiMessage());
        _window.OnClear  += category => SendMessage(new ClearLagEuiMsg.DoClear { Category = category });
        _window.OnRefresh += () => SendMessage(new ClearLagEuiMsg.RequestRefresh());
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not ClearLagEuiState s)
            return;

        _window.UpdateCounts(s.TrashCount, s.SpentCasingsCount, s.SovietArmsCratesCount);
    }
}
