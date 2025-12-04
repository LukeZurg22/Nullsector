using Content.Client._Null.Nullith.UI;
using Content.Shared._Null.Nullith;
using Content.Shared._Null.Nullith.Events;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface.Controls;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Content.Client._Null.Nullith.BUI;

/// <inheritdoc />
public sealed class NullithConsoleBoundUserInterface : BoundUserInterface
{
    private NullithConsoleMenu? _menu;

    public int Balance { get; private set; }

    public NullithConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = new NullithConsoleMenu(this);
        _menu.OpenCentered();
        // The Shipyard Console may be used as reference for rules menu popup, if it needs implementation.
        //  Else-wise, it shall NOT here as it isn't needed as of 20251201.
        _menu.OnClose += Close;
        _menu.OnOrderApproved += ApproveOrder;
        _menu.TargetIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent("ShipyardConsole-targetId"));
    }

    private void Populate(List<string> availablePrototypes,
        List<string> unavailablePrototypes,
        bool freeListings,
        bool validId)
    {
        if (_menu == null)
            return;

        _menu.PopulateLocations(availablePrototypes, unavailablePrototypes, freeListings, validId);
        _menu.PopulateCategories(availablePrototypes, unavailablePrototypes);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not NullithConsoleInterfaceState consoleState)
            return;

        Balance = consoleState.Balance;
        Populate(
            consoleState.ShipyardPrototypes.available,
            consoleState.ShipyardPrototypes.unavailable,
            consoleState.FreeListings,
            consoleState.IsTargetIdPresent);
        _menu?.UpdateState(consoleState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;
        _menu?.Dispose();
    }

    private void ApproveOrder(BaseButton.ButtonEventArgs args)
    {
        if (args.Button.Parent?.Parent is not PoIRow row || row.PoI == null)
        {
            return;
        }

        var pointOfInterestId = row.PoI.ID;
        SendMessage(new NullithConsolePurchaseMessage(pointOfInterestId));
    }
}
