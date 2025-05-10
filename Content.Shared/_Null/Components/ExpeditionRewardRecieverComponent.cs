using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._Null.Components;

/// <summary>
/// Designates this entity as holding a salvage expedition.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ExpeditionRewardRecieverComponent : Component
{
    /// <summary>
    /// Where the palette or expedition computer is located for expedition reward placement.
    /// </summary>
    [DataField("spotLocation")]
    public Vector2 SpotLocation = Vector2.Zero;

    /// <summary>
    /// Where the palette or expedition computer is located for expedition reward placement.
    /// </summary>
    [DataField("owningStation")]
    public EntityUid OwningStation = default!;

    /// <summary>
    /// To determine whether this pallet is a depot pallet; one that expedition rewards should additionally send rewards to.
    /// </summary>
    [DataField("isDepot")]
    public bool IsDepot = false;

    /// <summary>
    /// The Prototype of the paper that receipts will be printed upon.
    /// </summary>
    public EntProtoId ReceiptOutput = "ExpedPaperReceipt";
}
