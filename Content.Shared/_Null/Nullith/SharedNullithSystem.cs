using System.Diagnostics.CodeAnalysis;
using Content.Shared._Null.Components;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Null.Nullith;

// Note: when adding a new ui key, don't forget to modify the dictionary in SharedShipyardSystem
[NetSerializable, Serializable]
public enum NullithConsoleUiKey : byte
{
    /// <summary>Represents purchasable Points of Interest in Nullith. NOT FOR SHIPS!</summary>
    Monolith,
    // TODO: various kinds of nullith exist. This could add exclusivity for certain kinds of PoIs per round.
    /// Add ships to this key if they are only available from mothership consoles. Shipyards using it are inherently empty and are populated using the ShipyardListingComponent.
    Custom,
}

public abstract class SharedNullithSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NullithConsoleComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<NullithConsoleComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<NullithConsoleComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<NullithConsoleComponent, ComponentHandleState>(OnHandleState);
    }

    private void OnHandleState(EntityUid uid, NullithConsoleComponent component, ref ComponentHandleState args)
    {
        // if (args.Current is not NullithConsoleComponentState)
        //     return;
    }

    private void OnGetState(EntityUid uid, NullithConsoleComponent component, ref ComponentGetState args)
    {

    }

    private void OnComponentInit(EntityUid uid, NullithConsoleComponent component, ComponentInit args)
    {
        _itemSlotsSystem.AddItemSlot(uid, NullithConsoleComponent.TargetIdCardSlotId, component.TargetIdSlot);
    }

    private void OnComponentRemove(EntityUid uid, NullithConsoleComponent component, ComponentRemove args)
    {
        _itemSlotsSystem.RemoveItemSlot(uid, component.TargetIdSlot);
    }

    [Serializable, NetSerializable]
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    private sealed class NullithConsoleComponentState : ComponentState
    {
        public List<string> AccessLevels;

        public NullithConsoleComponentState(List<string> accessLevels)
        {
            AccessLevels = accessLevels;
        }
    }

}
