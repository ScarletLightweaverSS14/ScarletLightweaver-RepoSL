using Robust.Shared.Configuration;

namespace Content.Shared.Starlight.CCVar;
public sealed partial class StarlightCCVars
{
    public static readonly CVarDef<bool> TracesEnabled =
        CVarDef.Create("opt.traces_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// When true, draws a screen-space overlay above each entity showing their ping / simulated lag info.
    /// Used for testing client-side gun prediction.
    /// </summary>
    public static readonly CVarDef<bool> PredictionDebugOverlay =
        CVarDef.Create("gun.prediction_debug", false, CVar.CLIENTONLY);
}
