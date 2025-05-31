using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._RMC14.CCVar;

// NOTE: This class is lifted from RMC14, however most CVars were removed.

[CVarDefs]
public sealed partial class RMCCVars : CVars
{
    public static readonly CVarDef<float> VolumeGainCassettes =
        CVarDef.Create("rmc.volume_gain_cassettes", 0.25f, CVar.REPLICATED | CVar.CLIENT | CVar.ARCHIVE);
}
