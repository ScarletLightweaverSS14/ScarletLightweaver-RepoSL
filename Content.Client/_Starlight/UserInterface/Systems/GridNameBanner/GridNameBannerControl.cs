using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Starlight.UserInterface.Systems.GridNameBanner;

/// <summary>
///     Transparent control that typewriters a location name at the top-centre
///     of the screen, holds it briefly, then erases it in reverse.
/// </summary>
public sealed class GridNameBannerControl : Control
{
    // ── Tuning ────────────────────────────────────────────────────────────────
    private const float TypeDelay   = 0.09f;  // seconds per character while typing
    private const float DeleteDelay = 0.045f; // seconds per character while erasing
    private const float HoldSeconds = 5.0f;   // seconds the full text stays visible

    // ── State machine ─────────────────────────────────────────────────────────
    private enum Phase { Idle, Typing, Holding, Deleting }

    private Phase  _phase       = Phase.Idle;
    private string _fullText    = "";
    private int    _visibleChars;
    private float  _elapsed;

    // ── Child controls ────────────────────────────────────────────────────────
    private readonly Label _label;

    public GridNameBannerControl()
    {
        MouseFilter = MouseFilterMode.Ignore;

        // 22-pt RobotoMono-Bold gives a clean terminal look fitting SS14's aesthetic.
        Font? font = null;
        try
        {
            var res  = IoCManager.Resolve<IResourceCache>();
            var path = new ResPath("/Fonts/RobotoMono/RobotoMono-Bold.ttf");
            font = new VectorFont(res.GetResource<FontResource>(path), 22);
        }
        catch
        {
            // If the font fails to load, fall back to the stylesheet default.
        }

        _label = new Label
        {
            Align               = Label.AlignMode.Center,
            FontColorOverride   = Color.White,
            FontOverride        = font,
            MouseFilter         = MouseFilterMode.Ignore,
        };
        AddChild(_label);

        Visible = false;
    }

    /// <summary>
    ///     Begin (or restart) the typewriter animation with the given text.
    ///     Newlines in <paramref name="text"/> are displayed as line-breaks.
    /// </summary>
    public void Show(string text)
    {
        _fullText     = text;
        _visibleChars = 0;
        _elapsed      = 0f;
        _phase        = Phase.Typing;
        _label.Text   = "";
        Visible       = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        switch (_phase)
        {
            case Phase.Typing:
            {
                _elapsed += args.DeltaSeconds;
                while (_elapsed >= TypeDelay && _visibleChars < _fullText.Length)
                {
                    _elapsed -= TypeDelay;
                    _visibleChars++;
                }
                _label.Text = _fullText[.._visibleChars];

                if (_visibleChars >= _fullText.Length)
                {
                    _elapsed = 0f;
                    _phase   = Phase.Holding;
                }
                break;
            }

            case Phase.Holding:
            {
                _elapsed += args.DeltaSeconds;
                if (_elapsed >= HoldSeconds)
                {
                    _elapsed = 0f;
                    _phase   = Phase.Deleting;
                }
                break;
            }

            case Phase.Deleting:
            {
                _elapsed += args.DeltaSeconds;
                while (_elapsed >= DeleteDelay && _visibleChars > 0)
                {
                    _elapsed -= DeleteDelay;
                    _visibleChars--;
                }
                _label.Text = _fullText[.._visibleChars];

                if (_visibleChars <= 0)
                {
                    _phase  = Phase.Idle;
                    Visible = false;
                }
                break;
            }
        }
    }
}
