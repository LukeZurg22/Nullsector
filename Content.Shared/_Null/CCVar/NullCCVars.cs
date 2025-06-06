using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Null.CCVar;

// NOTE: This class is lifted from RMC14, however most CVars were removed.

[CVarDefs]
public sealed partial class NullCCVars : CVars
{
    /// <summary>
    /// Null: Toggle Displaying Username in End of Round Summary Webhook(TM). False by default.
    /// </summary>
    public static readonly CVarDef<bool> DisplayUsernameInSummary =
        CVarDef.Create("discord.display_username_in_summary", false, CVar.REPLICATED | CVar.CLIENTONLY | CVar.ARCHIVE);
}
