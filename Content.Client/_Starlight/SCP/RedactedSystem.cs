using Robust.Client.Graphics;

namespace Content.Client._Starlight.SCP;

/// <summary>
/// Registers <see cref="RedactedOverlay"/> for the lifetime of the client session.
/// </summary>
public sealed class RedactedSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlayManager.AddOverlay(new RedactedOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<RedactedOverlay>();
    }
}
