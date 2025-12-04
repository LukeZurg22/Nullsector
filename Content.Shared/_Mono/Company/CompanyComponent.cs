using Robust.Shared.GameStates;

namespace Content.Shared._Mono.Company;

/// <summary>
/// Component that represents a player's affiliated company.<br/>
/// This component is also initialized onto ships that are bought by a player.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CompanyComponent : Component
{
    /// <summary>
    /// The name of the company the player belongs to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string CompanyName = string.Empty;

    // TODO: Make this useful for something. Chiefly Company stamps, such as the dynamic stamp in NullithSystem.Consoles.cs
    /// <summary>
    /// Assigns the Company's official color for use in stamping, and possible IFF changes. [UNUSED!!!]
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color CompanyColor;

    public const string NonExistentCompanyName = "None";
}
