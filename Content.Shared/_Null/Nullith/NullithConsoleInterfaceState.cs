using Robust.Shared.Serialization;

namespace Content.Shared._Null.Nullith;

[NetSerializable, Serializable]
public sealed class NullithConsoleInterfaceState : BoundUserInterfaceState
{
    public int Balance;
    public readonly bool AccessGranted;
    public readonly string? PoIDeedTitle;
    public readonly bool IsTargetIdPresent;
    public readonly byte UiKey;

    public readonly (List<string> available, List<string> unavailable) ShipyardPrototypes;
    public readonly string ShipyardName;
    public readonly bool FreeListings;

    public NullithConsoleInterfaceState(
        int balance,
        bool accessGranted,
        string? poIDeedTitle,
        bool isTargetIdPresent,
        byte uiKey,
        (List<string> available, List<string> unavailable) shipyardPrototypes,
        string shipyardName,
        bool freeListings,
        float sellRate)
    {
        Balance = balance;
        AccessGranted = accessGranted;
        PoIDeedTitle = poIDeedTitle;
        IsTargetIdPresent = isTargetIdPresent;
        UiKey = uiKey;
        ShipyardPrototypes = shipyardPrototypes;
        ShipyardName = shipyardName;
        FreeListings = freeListings;
    }
}
