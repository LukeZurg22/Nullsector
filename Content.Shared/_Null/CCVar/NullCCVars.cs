using Robust.Shared.Configuration;

namespace Content.Shared._Null.CCVar;

[CVarDefs]
public sealed class NullCCVars
{
    /// <summary>
    /// Null: Toggle Displaying Username in End of Round Summary Webhook(TM). False by default.
    /// </summary>
    public static readonly CVarDef<bool> DisplayUsernameInSummary =
        CVarDef.Create("display.display_username_in_summary", false, CVar.CLIENTONLY | CVar.ARCHIVE);

}
