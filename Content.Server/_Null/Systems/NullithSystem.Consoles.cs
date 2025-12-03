using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Mono.Shipyard;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Components;
using Content.Server._NF.ShuttleRecords;
using Content.Server._Null.Components;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.DeviceNetwork.Components;
using Content.Server.Fax;
using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Server.StationEvents.Components;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._Null.Components;
using Content.Shared._Null.Nullith;
using Content.Shared._Null.Nullith.Events;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Fax.Components;
using Content.Shared.Paper;
using Content.Shared.Radio;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Null.Systems;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "ArrangeMethodOrOperatorBody")]
public sealed partial class NullithSystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IdCardSystem _idSystem = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private readonly PaperSystem _stampSystem = default!;
    [Dependency] private readonly FaxSystem _faxSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ShuttleRecordsSystem _shuttleRecordsSystem = default!;

    public void InitializeConsole() { }

    private void OnPurchaseMessage(EntityUid nullithConsoleUid,
        NullithConsoleComponent nullithConsoleComponent,
        NullithConsolePurchaseMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        // Check if contained object is valid.
        if (nullithConsoleComponent.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } buyerId)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-idcard"));
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            return;
        }

        // Check if contained object is an ID card or voucher.
        TryComp<IdCardComponent>(buyerId, out var idCard);
        TryComp<NullithVoucherComponent>(buyerId, out var voucher);
        if (idCard is null && voucher is null)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-idcard"));
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            return;
        }

        // Check if user making the purchase has legitimate accesses to the console.
        if (TryComp<AccessReaderComponent>(nullithConsoleUid, out var accessReaderComponent) &&
            !_access.IsAllowed(player, nullithConsoleUid, accessReaderComponent))
        {
            ConsolePopup(player, Loc.GetString("comms-console-permission-denied"));
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            return;
        }

        if (!_prototypeManager.TryIndex<BuyablePoIPrototype>(args.PointOfInterest, out var pointOfInterestPrototype))
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-invalid-vessel", ("vessel", args.PointOfInterest)));
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            return;
        }

        if (!GetAvailableShuttles(nullithConsoleUid, targetId: buyerId).available.Contains(pointOfInterestPrototype.ID))
        {
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            _adminLogger.Add(LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(player):player} tried to purchase a vessel that was never available.");
            return;
        }

        var name = pointOfInterestPrototype.Name;
        if (pointOfInterestPrototype.Price <= 0)
            return;

        if (_station.GetOwningStation(nullithConsoleUid) is not { Valid: true } station)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-invalid-station"));
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            return;
        }

        if (!TryComp<BankAccountComponent>(player, out var bank))
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-bank"));
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            return;
        }

        // Keep track of whether a voucher was used, or not.
        var voucherUsed = false;
        if (voucher is not null)
        {
            if (TryPurchaseWithVoucher(nullithConsoleUid,
                    nullithConsoleComponent,
                    args,
                    voucher,
                    player,
                    buyerId,
                    ref voucherUsed))
                return;
        }
        else
        {
            if (UserIsPoor(nullithConsoleUid, nullithConsoleComponent, bank, pointOfInterestPrototype, player))
                return; // Get bent. No Voucher will save you here.
        }

        // Attempt to purchase the Buyable Point of Interest. After this point, the remainder should be handling-
        //  -printing deeds or any secondary affects.
        var nullithMap = _transform.GetMapId(nullithConsoleUid); // Get map the Monolith belongs to.
        if (!TryPurchaseLocation((station, nullithMap), pointOfInterestPrototype, out var pointOfInterest_Unchecked))
        {
            PlayDenySound(player, nullithConsoleUid, nullithConsoleComponent);
            return;
        }

        // The thing was purchased. Add this sucker to the Already-Purchased list!
        AlreadyPurchasedPointsOfInterest.Add(pointOfInterestPrototype.ID);

        // === === === === === === === === === === === === === === === === === === === === === === === === === === ===
        // For absolute certainty the value of the Point of Interest is valid beyond this point.
        var pointOfInterest = pointOfInterest_Unchecked.Value;

        /* ===Commentary===
         * As far as I am concerned, purchasable PoI's could be dedicated to certain organizations. However, the idea of
         * players individually purchasing and changing their own bases of operations sounds awesome to me, and I do quite
         * like the implications of dedicated organizational spaces without the context of constant fighting and "warfare".
         * As such, I would like to keep this in, if only to make things more... territorially interesting—in a setting
         * that prides itself on territorial disputes—for the Null Sector.
         *  Kindly,
         *  -LZ22
         */
        // Add company information to the location
        var sponsor = Loc.GetString("nullith-deed-company-nonexistent");
        if (TryComp<CompanyComponent>(player, out var playerCompany) && // Player company component
            !string.IsNullOrEmpty(playerCompany.CompanyName) && // The company isn't null or empty
            !playerCompany.CompanyName.Equals(CompanyComponent
                .NonExistentCompanyName)) // Just in case, it doesn't say "None"
        {
            // Assign the sponsor for the deed.
            sponsor = Loc.GetString("nullith-deed-company-exists", ("company", playerCompany.CompanyName));
            // Handle ship's company information.
            var shipCompany = EnsureComp<CompanyComponent>(pointOfInterest);
            shipCompany.CompanyName = playerCompany.CompanyName;
            Dirty(pointOfInterest, shipCompany);
        }

        #region Obsolete Code (just in case)

        /* // Null Sector - Points of Interests are not shuttles. They tend not to move.
        // Ensure that the vessel contains a shuttle component
         if (!_entityManager.TryGetComponent<ShuttleComponent>(pointOfInterestID_Validated, out _))
        {
            PlayDenySound(player, shipyardConsoleUid, component);
            return;
        }*/


        /*
         // Null Sector - This spawns a spare copy of the POI, which is BAD. However it also handles station things, so if
            // If there are stations, they must be set-up assuming there is a matching game-map prototype.
            // This allows late-joins the ability to directly join onto the location, should it be available.
         EntityUid? poiStation = null;
        if (_prototypeManager.TryIndex<GameMapPrototype>(pointOfInterestPrototype.ID, out var stationProto))
        {
            List<EntityUid> gridUids = [pointOfInterest];
            poiStation = _station.InitializeNewStation(stationProto.Stations[pointOfInterestPrototype.ID], gridUids);
            name = Name(poiStation.Value);
        }*/

        /*Null Sector -
         * Access changes not really required. This should be improved later, of course, should certain locations have
         * special buyer-only doors.
         */
        /*
        // Assign accesses.
        if (TryComp<AccessComponent>(buyerId, out var accessComponent))
        {
            var newAccess = accessComponent.Tags.ToList();
            newAccess.AddRange(component.NewAccessLevels);
            _accessSystem.TrySetTags(buyerId, newAccess, accessComponent);
        }*/

        // Null Sector - No IDCard-driven Deeds necessary here. But just in case, the code is left behind.
        /*
        var deedID = EnsureComp<ShuttleDeedComponent>(buyerId);

        AssignShuttleDeedProperties(deedID, pointOfInterestID_Validated, name, player, voucherUsed);
        //deedID.DeedHolderCard = targetId;

        var deedShuttle = EnsureComp<ShuttleDeedComponent>(pointOfInterestID_Validated);
        AssignShuttleDeedProperties(deedShuttle, pointOfInterestID_Validated, name, player, voucherUsed);
        */

        #endregion

        EntityManager.AddComponents(pointOfInterest, pointOfInterestPrototype.AddComponents);

        // Ensure cleanup on ship sale
        EnsureComp<LinkedLifecycleGridParentComponent>(pointOfInterest);

        CompletePurchase(nullithConsoleUid,
            nullithConsoleComponent,
            sponsor,
            pointOfInterest,
            player,
            name,
            voucherUsed,
            buyerId,
            pointOfInterestPrototype,
            playerCompany);

        RefreshState(nullithConsoleUid,
            bank.Balance,
            true,
            name,
            buyerId,
            (NullithConsoleUiKey)args.UiKey,
            voucherUsed);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="monolith">The monolith itself.</param>
    /// <param name="component">The NullithConsoleComponent belonging to the Monolith.</param>
    /// <param name="sponsor">Localized deed string of the company sponsoring the player.</param>
    /// <param name="pointOfInterest">The ID of the PoI being purchased</param>
    /// <param name="player">The Player entity</param>
    /// <param name="locationName">The name of the location</param>
    /// <param name="voucherUsed">Checks if a voucher was used to get it for free</param>
    /// <param name="buyerID">The player's ID card (or voucher) itself</param>
    /// <param name="poiProto">Prototype of the purchased POI</param>
    /// <param name="company">The company that the player belongs to.</param>
    private void CompletePurchase(EntityUid monolith,
        NullithConsoleComponent component,
        string sponsor,
        EntityUid pointOfInterest,
        EntityUid player,
        string locationName,
        bool voucherUsed,
        EntityUid buyerID,
        BuyablePoIPrototype poiProto,
        CompanyComponent? company)
    {
        // Example: "Zombie Lab Landowner"; makes their title clean. Else, use the provided title localization.
        // The title bestowed upon the player.
        var title =
            string.IsNullOrEmpty(poiProto.TitleLoc)
                ? $"{poiProto.Name} {Loc.GetString("poi-title-blank")}"
                : Loc.GetString(poiProto.TitleLoc);

        // The complete and formalized deed that the player is provided.
        var coords = _transform.GetWorldPosition(pointOfInterest);
        var poiDeed = Loc.GetString(
            "nullith-console-deed",
            ("player", Name(player)), // Get player's name directly.
            ("title", title),
            ("sponsor", sponsor),
            ("location", locationName),
            ("coordinates", $"{Math.Round(coords.X,0)}, {Math.Round(coords.Y,0)}")); // Rounding for full coordinates.

        #region Creating the Stamp Overlay for The Deed

        var companyStamp = new StampDisplayInfo();
        if (company is not null && company.CompanyName != CompanyComponent.NonExistentCompanyName)
        {
            // TODO: Companies do not have colours applied to them. For now, assigns stamp color to that of IFF.
            if (TryComp<IFFComponent>(pointOfInterest, out var iffComponent))
                companyStamp.StampedColor = iffComponent.Color;
            companyStamp.StampedName = company.CompanyName;
        }
        // The player isn't part of a legitimate company.
        else
        {
            companyStamp.StampedName = Loc.GetString("nullith-deed-company-default");
            companyStamp.StampedColor = Color.Black;
        }

        #endregion

        #region Creating the Deed

        // Begin with attempting to find the fax machine.
        EntityUid? destinationFax = null;
        var faxMachines = EntityManager.EntityQueryEnumerator<FaxMachineComponent, DeviceNetworkComponent>();
        while (faxMachines.MoveNext(out var uid, out _, out _))
        {
            var faxGrid = _transform.GetGrid(uid);
            if (faxGrid is null)
                continue;
            if (!TryComp<ShuttleDeedComponent>(faxGrid.Value, out var shuttleDeed) ||
                shuttleDeed.DeedHolderCard is null)
                continue;
            if (shuttleDeed.DeedHolderCard.Value.Id == buyerID.Id)
            {
                destinationFax = uid;
            }
        }

        // If there is a place to fax the Deed to, fax it there.
        string deedSpawnMessage;
        if (destinationFax is not null)
        {
            deedSpawnMessage = Loc.GetString("nullith-deed-provided-ship-success", ("ship", destinationFax.Value));
            var printout = new FaxPrintout(
                poiDeed,
                Loc.GetString("nullith-deed-name", ("location", Loc.GetString(poiProto.Name))),
                null,
                DeedPrototype,
                DefaultStampState,
                [companyStamp],
                locked: true,
                stampProtected: true);
            _faxSystem.Receive(destinationFax.Value, printout);
        }
        // If the player doesn't have a valid ship, then just create it at the Monolith.
        else
        {
            // Play a message and spawn the deed manually. Assumes the deed indeed includes a paper component.
            //  It better!
            deedSpawnMessage = Loc.GetString("nullith-deed-provided-ship-failure");
            var deed = EntityManager.SpawnAttachedTo(DeedPrototype, Transform(monolith).Coordinates);
            var paperComponent = Comp<PaperComponent>(deed);
            paperComponent.StampedBy = [companyStamp];
            paperComponent.StampState = DefaultStampState;
            paperComponent.EditingDisabled = true;
            paperComponent.Content = poiDeed;
            _metaData.SetEntityName(deed, Loc.GetString("nullith-deed-name", ("location", poiProto.Name))); // Set custom name for deed. "Deed to X"
        }

        // Send deed spawn message.
        if (_playerManager.TryGetSessionByEntity(player, out var session))
        {
            _chatManager.ChatMessageToOne(ChatChannel.Server,
                deedSpawnMessage,
                deedSpawnMessage,
                EntityUid.Invalid,
                false,
                session.Channel);
        }

        #endregion

        #region Creating the Personalized Stamp

        var personalizedStamp = EntityManager.SpawnAttachedTo(GenericStampPrototype, Transform(monolith).Coordinates);
        var stampComponent = Comp<StampComponent>(personalizedStamp);
        stampComponent.StampedColor = TryComp<IFFComponent>(pointOfInterest, out var iff)
            ? iff.Color // Uses IFF color for stamp. Very appropriate!
            : Color.Black; // Defaults to black for personalized stamp.
        stampComponent.StampedName = title; // Behold, the players awesome new custom title stamp!
        //stampComponent.StampState // This is unchanged. The default paper_stamp-company is used for now, but perhaps
        //  in the future, dynamic stamps could allow some creative stamp types based on POI type.
        // Changing the sprite referenced would also mean some unique on-paper visual stamps. For now though, this will do.
        _metaData.SetEntityName(personalizedStamp, $"{title} stamp"); // Set custom name for stamp.

        #endregion

        SendPurchaseMessage(monolith, player, locationName, component.ShipyardChannel, secret: false);
        if (component.SecretShipyardChannel is { } secretChannel)
            SendPurchaseMessage(monolith, player, locationName, secretChannel, secret: true);

        // Mono -> Null Sector [    fixed it for ya' :)    ]
        _entitySystemManager.GetEntitySystem<ShipyardInformPurchaseLocationSystem>()
            .SendShipLocationMessage(player, pointOfInterest);

        PlayConfirmSound(player, monolith, component);

        #region Admin-Logging

        if (voucherUsed)
        {
            _adminLogger.Add(LogType.ShipYardUsage,
                LogImpact.Low,
                $"{ToPrettyString(player):actor} used {ToPrettyString(buyerID)} to purchase POI {ToPrettyString(pointOfInterest)} with a voucher via {ToPrettyString(monolith)}");
        }
        else
        {
            _adminLogger.Add(LogType.ShipYardUsage,
                LogImpact.Low,
                $"{ToPrettyString(player):actor} used {ToPrettyString(buyerID)} to purchase POI {ToPrettyString(pointOfInterest)} for {poiProto.Price} credits via {ToPrettyString(monolith)}");
        }

        #endregion
    }

    private bool UserIsPoor(EntityUid shipyardConsoleUid,
        NullithConsoleComponent component,
        BankAccountComponent bank,
        BuyablePoIPrototype pointOfInterest,
        EntityUid player)
    {
        // User is too poor. WEAK! WEAK! WEAK! WEAK! WEAK! WEAK! WEAK! WEAK! WEAK! WEAK!
        if (bank.Balance <= pointOfInterest.Price)
        {
            ConsolePopup(player,
                Loc.GetString("cargo-console-insufficient-funds", ("cost", pointOfInterest.Price)));
            PlayDenySound(player, shipyardConsoleUid, component);
            return true;
        }

        // User cannot withdraw from bank, somehow. Likely too poor.
        if (!_bank.TryBankWithdraw(player, pointOfInterest.Price))
        {
            ConsolePopup(player,
                Loc.GetString("cargo-console-insufficient-funds", ("cost", pointOfInterest.Price)));
            PlayDenySound(player, shipyardConsoleUid, component);
            return true;
        }

        return false;
    }

    private bool TryPurchaseWithVoucher(EntityUid shipyardConsoleUid,
        NullithConsoleComponent component,
        NullithConsolePurchaseMessage args,
        NullithVoucherComponent voucher,
        EntityUid player,
        EntityUid targetId,
        ref bool voucherUsed)
    {
        if (voucher.RedemptionsLeft <= 0)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-voucher-redemptions"));
            PlayDenySound(player, shipyardConsoleUid, component);
            if (voucher.DestroyOnEmpty)
            {
                QueueDel(targetId);
            }

            return true;
        }

        if (voucher.MonolithType != (NullithConsoleUiKey)args.UiKey)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-invalid-voucher-type"));
            PlayDenySound(player, shipyardConsoleUid, component);
            return true;
        }

        voucher.RedemptionsLeft--;
        voucherUsed = true;
        return false;
    }

    private void OnConsoleUIOpened(EntityUid uid, NullithConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (!component.Initialized)
            return;

        // Orthodox method. We need to update the UI when an ID is entered, but the UI needs to know the player-
        //  -character's bank account.
        if (!TryComp<ActivatableUIComponent>(uid, out var uiComp) || uiComp.Key == null)
            return;

        // Ensure that the user is a real player. Else, give up.
        if (args.Actor is not { Valid: true } player)
            return;

        // Make sure that the player has a bank account in order to use this thing.
        if (!TryComp<BankAccountComponent>(player, out var bank))
            return;

        var targetId = component.TargetIdSlot.ContainerSlot?.ContainedEntity;

        var voucherUsed = HasComp<ShipyardVoucherComponent>(targetId);

        string? fullName = null;
        RefreshState(uid,
            bank.Balance,
            true,
            fullName,
            targetId,
            (NullithConsoleUiKey)args.UiKey,
            voucherUsed);
    }

    private void ConsolePopup(EntityUid uid, string text) => _popup.PopupEntity(text, uid);

    private void SendPurchaseMessage(EntityUid uid, EntityUid player, string name, string shipyardChannel, bool secret)
    {
        var channel = _prototypeManager.Index<RadioChannelPrototype>(shipyardChannel);

        if (secret)
        {
            _chat.TrySendInGameICMessage(uid,
                Loc.GetString("nullith-console-purchased-secret"),
                InGameICChatType.Speak,
                true);
        }
        else
        {
            _radio.SendRadioMessage(uid,
                Loc.GetString("nullith-console-purchased", ("owner", player), ("location", name)),
                channel,
                uid);
            _chat.TrySendInGameICMessage(uid,
                Loc.GetString("nullith-console-purchased", ("owner", player), ("location", name)),
                InGameICChatType.Speak,
                true);
        }
    }

    private void PlayDenySound(EntityUid playerUid, EntityUid consoleUid, NullithConsoleComponent component)
        => _audio.PlayEntity(component.ErrorSound, playerUid, consoleUid);

    private void PlayConfirmSound(EntityUid playerUid, EntityUid consoleUid, NullithConsoleComponent component)
        => _audio.PlayEntity(component.ConfirmSound, playerUid, consoleUid);

    private void OnItemSlotChanged(EntityUid uid, NullithConsoleComponent component, ContainerModifiedMessage args)
    {
        if (!component.Initialized)
            return;

        if (args.Container.ID != component.TargetIdSlot.ID)
            return;

        // kind of cursed. We need to update the UI when an Id is entered, but the UI needs to know the player characters bank account.
        if (!TryComp<ActivatableUIComponent>(uid, out var uiComp) || uiComp.Key == null)
            return;

        var uiUsers = _ui.GetActors(uid, uiComp.Key);

        foreach (var user in uiUsers)
        {
            if (user is not { Valid: true } player)
                continue;

            if (!TryComp<BankAccountComponent>(player, out var bank))
                continue;

            var targetId = component.TargetIdSlot.ContainerSlot?.ContainedEntity;

            if (TryComp<ShuttleDeedComponent>(targetId, out var deed))
            {
                if (Deleted(deed.ShuttleUid))
                {
                    RemComp<ShuttleDeedComponent>(targetId.Value);
                    continue;
                }
            }

            var voucherUsed = HasComp<ShipyardVoucherComponent>(targetId);

            var fullName = deed != null ? GetFullName(deed) : null;
            RefreshState(uid,
                bank.Balance,
                true,
                fullName,
                targetId,
                (NullithConsoleUiKey)uiComp.Key,
                voucherUsed);
        }
    }

    private struct IDShipAccesses
    {
        public IReadOnlyCollection<ProtoId<AccessLevelPrototype>> Tags;
        public IReadOnlyCollection<ProtoId<AccessGroupPrototype>> Groups;
    }

    /// <summary>
    ///   Returns all shuttle prototype IDs the given shipyard console can offer.
    /// </summary>
    [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
    public (List<string> available, List<string> unavailable) GetAvailableShuttles(EntityUid uid,
        NullithConsoleUiKey? key = null,
        PurchasablePoIListingComponent? listing = null,
        EntityUid? targetId = null)
    {
        var available = new List<string>();
        var unavailable = new List<string>();

        if (key == null && TryComp<UserInterfaceComponent>(uid, out var ui))
        {
            // Try to find UI key that is an instance of the shipyard console UI key.
            foreach (var (k, _) in ui.Actors)
            {
                if (k is not NullithConsoleUiKey shipyardKey)
                    continue;
                key = shipyardKey;
                break;
            }
        }

        // No listing provided, try to get the current one from the console being used as a default.
        if (listing is null)
            TryComp(uid, out listing);

        // Construct access set from input type (voucher or ID card)
        IDShipAccesses accesses;
        var initialHasAccess = true;
        if (TryComp<NullithVoucherComponent>(targetId, out var voucher))
        {
            if (voucher.MonolithType == key)
            {
                accesses.Tags = voucher.Access;
                accesses.Groups = voucher.AccessGroups;
            }
            else
            {
                accesses.Tags = new HashSet<ProtoId<AccessLevelPrototype>>();
                accesses.Groups = new HashSet<ProtoId<AccessGroupPrototype>>();
                initialHasAccess = false;
            }
        }
        else if (TryComp<AccessComponent>(targetId, out var accessComponent))
        {
            accesses.Tags = accessComponent.Tags;
            accesses.Groups = accessComponent.Groups;
        }
        else
        {
            accesses.Tags = new HashSet<ProtoId<AccessLevelPrototype>>();
            accesses.Groups = new HashSet<ProtoId<AccessGroupPrototype>>();
        }

        foreach (var pointOfInterest in _prototypeManager.EnumeratePrototypes<BuyablePoIPrototype>())
        {
            var hasAccess = initialHasAccess;
            // If the vessel needs access to be bought, check the user's access.
            if (!string.IsNullOrEmpty(pointOfInterest.Access))
            {
                // Check tags. Naturally false by default, but any tags containing an access will flag this as true.
                hasAccess = accesses.Tags.Contains(pointOfInterest.Access);

                // Check each group if we haven't found access already.
                if (!hasAccess)
                {
                    foreach (var groupId in accesses.Groups)
                    {
                        var groupProto = _prototypeManager.Index(groupId);
                        if (groupProto?.Tags.Contains(pointOfInterest.Access) ?? false)
                        {
                            hasAccess = true;
                            break;
                        }
                    }
                }
            }

            // A set of stringent requirements that ensures that a POI can be purchased, assuming all goes well:
            //  - Its key isn't custom, and its group is equal to the key provided. (Limits to certain UIs)
            //  - The POI is in a valid listing of Points of Interest and the key isn't null.
            //  - Inverting the "IsPurchased" to ensure that it CANNOT show if it's already bought.
            // ~Kindly, LZ22
            var keyIsPartOfGroup = key != NullithConsoleUiKey.Custom && pointOfInterest.Group == key;
            var poiInPointsOfInterestList =
                listing?.PointsOfInterest.Contains(pointOfInterest.ID) == true || listing == null;
            var poiAlreadyPurchased = AlreadyPurchasedPointsOfInterest.Contains(pointOfInterest.ID);
            var isValid = keyIsPartOfGroup && poiInPointsOfInterestList && !poiAlreadyPurchased;
            if (!isValid)
                continue;
            if (hasAccess)
                available.Add(pointOfInterest.ID);
            else
                unavailable.Add(pointOfInterest.ID);
        }

        return (available, unavailable);
    }

    private void RefreshState(EntityUid uid,
        int balance,
        bool access,
        string? shipDeed,
        EntityUid? targetId,
        NullithConsoleUiKey uiKey,
        bool freeListings)
    {
        var newState = new NullithConsoleInterfaceState(
            balance,
            access,
            shipDeed,
            targetId.HasValue,
            ((byte)uiKey),
            GetAvailableShuttles(uid, uiKey, targetId: targetId),
            uiKey.ToString(),
            freeListings,
            0);

        _ui.SetUiState(uid, uiKey, newState);
    }
}
