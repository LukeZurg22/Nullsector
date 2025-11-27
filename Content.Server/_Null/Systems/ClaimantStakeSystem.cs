using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Server._Null.Components;
using Content.Server.Explosion.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Components.GC;
using Content.Shared._Null.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Content.Shared.Tiles;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Null.Systems;

public sealed class ClaimantStakeSystem : SharedClaimantStakeSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly INullExtensionSystem _nullExt = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClaimantStakeComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<ClaimantStakeComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ClaimantStakeComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<ClaimantStakeComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<ClaimantStakeComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ClaimantStakeComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnComponentStartup(Entity<ClaimantStakeComponent> ent, ref ComponentStartup args)
    {
        // Set initial power state
        if (TryComp<ApcPowerReceiverComponent>(ent, out var powerReceiver))
            ent.Comp.Enabled = powerReceiver.Powered;
    }

    private void OnActivate(Entity<ClaimantStakeComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-too-complex-popup"), ent, PopupType.MediumCaution);
            return;
        }

        // If the thing is already enabled, and it has no claimed grid, and it CAN be enabled,
        //  it is reasonable to assume the user wants to claim the grid rather than turn it off.
        var canEnable = CanEnable(ent, allowEnabledComp: true);
        if (ent.Comp.Enabled && ent.Comp.ClaimedGrid == null && canEnable)
        {
            BeginHandleClaim(ent, args.User);
            args.Handled = true;
            return;
        }
        // From here, it is assuming the user is aiming to toggle the machine rather than establish a claim.

        // Resetting grid claimant in advance.
        ent.Comp.ClaimedGrid = null;
        ent.Comp.Enabled ^= true;

        if (!ent.Comp.Enabled)
        {
            Disable(ent);
        }
        else if (CanEnable(ent))
        {
            Enable(ent);
        }
        args.Handled = true;
    }

    public bool CanEnable(Entity<ClaimantStakeComponent> ent, bool allowEnabledComp = false)
    {
        if (IsInvalidWreck(ent, out _, out _))
            return false;

        // If it is actively turning off or on, it is best not to interrupt the Claimant Stake.
        //  The user can only interact with it only when it's fully on or offline.
        if (ent.Comp.StakeStatus is not (ClaimantStakeStatus.Offline or ClaimantStakeStatus.Online))
            return false;

        if (allowEnabledComp == false && ent.Comp.Enabled == false)
            return false;

        // If it is receiving power then it should be fine.
        if (TryComp<ApcPowerReceiverComponent>(ent, out var powerReceiver))
        {
            // Disabling power forcefully happens to screw with power reception. If it's not disabled then it goes to-
            //  -show that it must be receiving power in order to work. If it is already disabled then who cares, go-
            //  -enable it.
            if (!powerReceiver.PowerDisabled && powerReceiver.Load > powerReceiver.PowerReceived)
            {
                _popup.PopupEntity(Loc.GetString("claimant-stake-no-power-popup"), ent, PopupType.SmallCaution);
                return false;
            }
        }

        return true;
    }

    private void OnPowerChanged(Entity<ClaimantStakeComponent> ent, ref PowerChangedEvent args)
    {
        ent.Comp.Enabled = args.Powered;
        if (IsInvalidWreck(ent, out _, out _))
        {
            ent.Comp.Enabled = false;
            return;
        }

        // Device was DISABLED
        if (ent.Comp.Enabled == false && ent.Comp.ClaimedGrid != null)
        {
            BeginHandleClaim(ent, claimant: null);
        }
    }

    [SuppressMessage("ReSharper", "RedundantJumpStatement")]
    private bool IsInvalidWreck(Entity<ClaimantStakeComponent> ent,
        out EntityUid? gridId,
        out MetaDataComponent? outMeta)
    {
        gridId = null;
        outMeta = null;

        // Claimant Stake Anchoring
        var xform = Transform(ent);
        if (!xform.Anchored)
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-unanchored-popup"), ent, PopupType.SmallCaution);
            return false;
        }

        // If there's no transform or no grid, it's invalid for claiming.
        if (!TryComp(ent, out TransformComponent? transform) || transform.GridUid == null)
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-invalid-wreck-popup"), ent, PopupType.MediumCaution);
            return true;
        }

        var currentGrid = transform.GridUid;

        // Require metadata and parents to be present.
        if (!TryComp(currentGrid, out MetaDataComponent? metaData) || metaData.EntityPrototype?.Parents == null)
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-invalid-wreck-popup"), ent, PopupType.MediumCaution);
            return true;
        }

        // Ensure the grid's prototype parents include the allowed wreck prototype.
        var parents = metaData.EntityPrototype.Parents.ToList();
        if (!parents.Any(prototype => prototype.Equals(ClaimantStakeComponent.WreckagePrototype)))
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-invalid-wreck-popup"), ent, PopupType.MediumCaution);
            return true;
        }

        // If it's already the same claimed grid, it's invalid to claim.
        if (currentGrid.Value == ent.Comp.ClaimedGrid)
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-repeat-claim-popup"), ent, PopupType.MediumCaution);
            return true;
        }

        // If there are other similar stakes on the same grid, declaring is forbidden.
        if (_nullExt.SimilarEntitiesArePresentOnGrid(ent))
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-alone-popup"), ent, PopupType.Medium);
            return true;
        }

        gridId = currentGrid;
        outMeta = metaData;
        return false; // Not invalid
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ClaimantStakeComponent>();
        while (query.MoveNext(out var uid, out var claimantStake))
        {
            switch (claimantStake.StakeStatus)
            {
                case ClaimantStakeStatus.Warming:
                    TickTimer(frameTime, (uid, claimantStake));
                    break;
                case ClaimantStakeStatus.Declaiming:
                    TickTimer(frameTime, (uid, claimantStake), isDeclaiming: true);
                    break;
            }
        }
    }

    // Keep this accepting Entity<ClaimantStakeComponent> to match other methods that use that wrapper.
    private void TickTimer(float frameTime, Entity<ClaimantStakeComponent> ent, bool isDeclaiming = false)
    {
        ent.Comp.RemainingTime -= frameTime;
        if (ent.Comp.RemainingTime >= 0)
            return;

        var stakeLocation = Transform(ent).Coordinates;
        ent.Comp.RemainingTime = ClaimantStakeComponent.AwaitBeepSoundTime;
        string popup;
        SoundSpecifier? playSound;
        if (isDeclaiming)
        {
            ent.Comp.StakeStatus = ClaimantStakeStatus.Offline;
            DeclaimGrid(ent);

            popup = Loc.GetString("claimant-stake-claim-erased-popup");
            playSound = ent.Comp.ShutdownSound;
            this.Disable(EntityManager, ent);
        }
        else
        {
            ent.Comp.StakeStatus = ClaimantStakeStatus.Online;
            ClaimGrid(ent);

            popup = Loc.GetString("claimant-stake-claim-success");
            playSound = ent.Comp.SoundAwake;
        }

        _audio.PlayPvs(playSound, stakeLocation);
        _popup.PopupEntity(popup, ent, PopupType.Large);
    }

    private void OnComponentInit(Entity<ClaimantStakeComponent> ent, ref ComponentInit args)
    {
        EnsureComp<TriggerOnActivateComponent>(ent);

        ent.Comp.SoundUse ??=
            new SoundPathSpecifier(
                ClaimantStakeComponent.DefaultUseSound,
                ClaimantStakeComponent.DefaultAudioParameters);
        ent.Comp.SoundAwake ??=
            new SoundPathSpecifier(
                ClaimantStakeComponent.DefaultAwakeSound,
                ClaimantStakeComponent.DefaultAudioParameters);
        ent.Comp.ShutdownSound ??=
            new SoundPathSpecifier(
                ClaimantStakeComponent.DefaultShutdownSound,
                ClaimantStakeComponent.DefaultAudioParameters);

        _popup.PopupEntity(Loc.GetString("claimant-stake-prompt-user-popup"), ent);
//Dirty(ent);
    }

    private void Enable(Entity<ClaimantStakeComponent> ent)
    {
        this.Enable(EntityManager, ent);
        _popup.PopupEntity(Loc.GetString("claimant-stake-activated-popup"), ent, PopupType.Medium);
    }

    private void Disable(Entity<ClaimantStakeComponent> ent)
    {
        BeginHandleClaim(ent, claimant: null);
    }

    #region Grid Claiming & Declaiming

    /// <summary>
    /// Handles the INITIALIZATION of claiming and un-claiming of wreckage. Ticker handles the rest.
    /// </summary>
    /// <param name="ent">The claimant stake itself.</param>
    /// <param name="claimant">If the claimant is null, this acts as a toggle to declaim a wreck.</param>
    private void BeginHandleClaim(Entity<ClaimantStakeComponent> ent, EntityUid? claimant = null)
    {
        // If it is actively turning off or on, it is best not to interrupt the Claimant Stake.
        //  The user can only interact with it only when it's fully on or offline.
        if (ent.Comp.StakeStatus is not (ClaimantStakeStatus.Offline or ClaimantStakeStatus.Online))
            return;

        var stakeLocation = Transform(ent.Owner).Coordinates;
        ent.Comp.PlayerOwner = claimant;
        SoundSpecifier? playSound;
        if (claimant != null)
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-claim-pending"), ent, PopupType.Medium);
            playSound = ent.Comp.SoundUse;
            ent.Comp.StakeStatus = ClaimantStakeStatus.Warming;
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("claimant-stake-shutdown-popup"), ent, PopupType.Medium);
            playSound = ent.Comp.ShutdownSound;
            ent.Comp.StakeStatus = ClaimantStakeStatus.Declaiming;
        }

        _audio.PlayPvs(playSound, stakeLocation);
    }

    private void ClaimGrid(Entity<ClaimantStakeComponent> ent, bool removeWreckComponents = true)
    {
        if (IsInvalidWreck(ent, out var currentGrid, out var metaData))
        {
            this.Disable(EntityManager, ent);
            BeginHandleClaim(ent, claimant: null); // Declaim the grid.
            return;
        }

        ent.Comp.ClaimedGrid = currentGrid;

        #region Changes to Grid

        ent.Comp.OldGridName = metaData?.EntityName;
        _meta.SetEntityName(currentGrid!.Value,
            Loc.GetString("claimant-wreckage-name", ("user", GetClaimantName(ent.Comp.PlayerOwner))),
            metaData);

        if (TryComp(currentGrid, out IFFComponent? iff))
        {
            ent.Comp.OldColorHex = iff.Color.ToHexNoAlpha();
            ent.Comp.OldFlags = iff.Flags;
            if (!string.IsNullOrEmpty(ent.Comp.NewColorHex))
            {
                iff.SetColor(ent.Comp.NewColorHex);
            }

            iff.Flags = IFFFlags.IsPlayerShuttle;
        }

        if (removeWreckComponents)
        {
            RemComp<ProtectedGridComponent>(currentGrid.Value);
            RemComp<OwnedDebrisComponent>(currentGrid.Value);
            RemComp<GCAbleObjectComponent>(currentGrid.Value);
        }

        #endregion
    }

    private void DeclaimGrid(Entity<ClaimantStakeComponent> ent, bool ensureWreckComponents = true)
    {
        ent.Comp.PlayerOwner = null;
        ent.Comp.ClaimedGrid = null;
        ent.Comp.StakeStatus = ClaimantStakeStatus.Offline;

        if (!TryComp(ent, out TransformComponent? transform) || transform.GridUid == null)
            return;

        var currentGrid = transform.GridUid;
        if (TryComp(currentGrid, out MetaDataComponent? metaData) && metaData.EntityPrototype?.Parents != null)
        {
            if (!string.IsNullOrEmpty(ent.Comp.OldGridName))
            {
                _meta.SetEntityName(currentGrid.Value, ent.Comp.OldGridName, metaData);
            }
        }

        if (TryComp(currentGrid, out IFFComponent? iff))
        {
            if (!string.IsNullOrEmpty(ent.Comp.OldColorHex))
            {
                iff.SetColor(ent.Comp.OldColorHex);
            }

            iff.Flags = ent.Comp.OldFlags;
        }

        if (!ensureWreckComponents)
            return;

        EnsureComp<ProtectedGridComponent>(currentGrid.Value);
        EnsureComp<OwnedDebrisComponent>(currentGrid.Value);
        var comp = EnsureComp<GCAbleObjectComponent>(currentGrid.Value);
        comp.Queue = ClaimantStakeComponent.WreckageRemovalQueueName;
    }

    #endregion

    private void OnComponentShutdown(Entity<ClaimantStakeComponent> ent, ref ComponentShutdown args)
    {
        DeclaimGrid(ent, false);
    }

    private string GetClaimantName(EntityUid? uid)
    {
        var isValidId = uid != null;
        return !isValidId
            ? Loc.GetString("claimant-stake-default-user")
            : TryComp<MetaDataComponent>(uid, out var meta)
                ? meta.EntityName
                : Loc.GetString("claimant-stake-default-user");
    }

    private void OnExamined(Entity<ClaimantStakeComponent> ent, ref ExaminedEvent args)
    {
        StringBuilder stringBuilder = new();
        var hasPlayerClaimant = ent.Comp.PlayerOwner != null;
        var playerName = GetClaimantName(ent.Comp.PlayerOwner);
        stringBuilder.Append(Loc.GetString("claimant-stake-grid-claimant-examine", ("claimant", hasPlayerClaimant)));
        stringBuilder.Append(' ');
        stringBuilder.AppendLine(Loc.GetString("claimant-stake-examined-user", ("user", playerName)));
        args.PushMarkup(stringBuilder.ToString());
    }
}
