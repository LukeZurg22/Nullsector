using Content.Shared._Null.CCVar;
using Robust.Shared.Configuration;

namespace Content.Shared._Null.Systems;

public sealed class UserToggleRoundUsername : EntitySystem
{
    [Dependency] private   readonly IConfigurationManager _cfg = default!;
    public bool DisplayUsernameInSummary = true;

    public new void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, NullCCVars.DisplayUsernameInSummary, OnPreferenceChanged, true);
    }
    private void OnPreferenceChanged(bool obj)
    {
        DisplayUsernameInSummary = obj;
    }
}
