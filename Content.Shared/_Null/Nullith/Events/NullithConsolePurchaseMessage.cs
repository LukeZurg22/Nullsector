using Robust.Shared.Serialization;

namespace Content.Shared._Null.Nullith.Events;

/// <summary>
///     Purchase a Vessel from the console
/// </summary>
[Serializable, NetSerializable]
public sealed class NullithConsolePurchaseMessage : BoundUserInterfaceMessage
{
    public string PointOfInterest; // prototype ID

    public NullithConsolePurchaseMessage(string poi)
    {
        PointOfInterest = poi;
    }
}
