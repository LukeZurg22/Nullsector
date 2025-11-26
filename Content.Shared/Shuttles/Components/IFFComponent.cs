using System.Diagnostics.CodeAnalysis;
using Content.Shared._Null.Systems;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Shuttles.Components;

/// <summary>
/// Handles what a grid should look like on radar.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedShuttleSystem), typeof(SharedClaimantStakeSystem))]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed partial class IFFComponent : Component
{
    public static readonly Color SelfColor = Color.MediumSpringGreen;

    /// <summary>
    /// Default color to use for IFF if no component is found.
    /// </summary>
    public static readonly Color IFFColor = Color.Gold;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public IFFFlags Flags = IFFFlags.None;

    /// <summary>
    /// Color for this to show up on IFF.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public Color Color = IFFColor;

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public void SetColor(string HEX)
    {
        // Add # to ensure the hex conversion works.
        if (!HEX.StartsWith('#'))
        {
            HEX = '#' + HEX;
        }
        // Accounts for alpha values. Alpha is maximized.
        if (HEX.Length <= 7)
        {
            HEX += "FF";
        }
        var color = Color.FromHex(HEX);
        SetColor(color);
    }
    public void SetColor(Color color) => Color = color;

    // Frontier: POI IFF protection
    /// <summary>
    /// Whether this entity's IFF can be changed.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField(serverOnly: true)]
    public bool ReadOnly;
    // End Frontier
}

[Flags]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum IFFFlags : byte
{
    None = 0,

    /// <summary>
    /// Should the label for this grid be hidden at all ranges.
    /// </summary>
    HideLabel = 1,

    /// <summary>
    /// Should the grid hide entirely (AKA full stealth).
    /// Will also hide the label if that is not set.
    /// </summary>
    Hide = 2,

    /// <summary>
    /// Frontier - Is this a player shuttle
    /// </summary>
    IsPlayerShuttle = 4,

    // TODO: Need one that hides its outline, just replace it with a bunch of triangles or lines or something.
}
