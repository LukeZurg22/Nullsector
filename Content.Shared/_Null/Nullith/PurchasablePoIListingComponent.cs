using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Shared._Null.Nullith;

/// <summary>
/// When applied to a shipyard console, adds all specified shuttles to the list of sold shuttles.
/// </summary>
[RegisterComponent]
public sealed partial class PurchasablePoIListingComponent : Component
{
    /// <summary>
    ///   All VesselPrototype IDs that should be listed in this shipyard console.
    /// </summary>
    [ViewVariables, DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<BuyablePoIPrototype>))]
    public List<string> PointsOfInterest = [];
}
