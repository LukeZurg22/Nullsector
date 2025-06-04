using System.Linq;
using Content.Shared._Null.Components;
using Content.Shared._RMC14.CCVar;
using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._RMC14.Cassette;

public abstract class SharedCassetteSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly INetConfigurationManager _netConfig = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<CassettePlayerComponent, GetItemActionsEvent>(OnPlayerGetItemActions);
        SubscribeLocalEvent<CassettePlayerComponent, CassettePlayPauseActionEvent>(OnPlayerPlayPause);
        SubscribeLocalEvent<CassettePlayerComponent, CassetteNextActionEvent>(OnPlayerNext);
        SubscribeLocalEvent<CassettePlayerComponent, CassetteRestartActionEvent>(OnPlayerRestart);
        SubscribeLocalEvent<CassettePlayerComponent, InteractUsingEvent>(OnPlayerInteractUsing);
        SubscribeLocalEvent<CassettePlayerComponent, UseInHandEvent>(OnPlayerEjectHand);
        SubscribeLocalEvent<CassettePlayerComponent, GetEquipmentVisualsEvent>(OnPlayerGetEquipmentVisuals,
            after: [typeof(ClothingSystem)]);
        SubscribeLocalEvent<CassettePlayerComponent, GotUnequippedEvent>(OnPlayerUnequipped);
        SubscribeLocalEvent<HeadphonesComponent, GotUnequippedEvent>(OnHeadphonesUnequipped);
        SubscribeLocalEvent<CassettePlayerComponent, ExaminedEvent>(OnPlayerExamined);
        SubscribeLocalEvent<CassettePlayerComponent, AfterAutoHandleStateEvent>(OnPlayerState);
        SubscribeLocalEvent<CassettePlayerComponent, EntRemovedFromContainerMessage>(OnPlayerRemovedFromContainer);
        SubscribeLocalEvent<CassettePlayerComponent, GetVerbsEvent<AlternativeVerb>>(OnPlayerGetVerbsAlternative);

        SubscribeLocalEvent<CassetteTapeComponent, ExaminedEvent>(OnTapeExamined);
        //SubscribeLocalEvent<CassetteTapeComponent, UseInHandEvent>(OnPlayerUseInHand); //WIP: Unfinished.
        SubscribeLocalEvent<CassetteTapeComponent, GetVerbsEvent<AlternativeVerb>>(OnTapeGetVerbsAlternative);
    }

    // IMPL: cassette no-headphones popup doesnt show, probably because of some weird conflict.
        // IMPL: a possible solution is to specifically check _net.isClient to see if its the client receiving it,
        // IMPL: then do Popup to Entity, rather than Client.

    /// <summary>
    /// Stops music whenever headphones are removed.
    /// </summary>
    private void OnHeadphonesUnequipped(Entity<HeadphonesComponent> ent, ref GotUnequippedEvent args)
        => RemoveAudioFromEquipee(args.Equipee);

    /// <summary>
    /// Stops music whenever the actual player is detached.
    /// </summary>
    private void OnPlayerDetached(PlayerDetachedEvent ev) => RemoveAudioFromEquipee(ev.Entity);

    /// <summary>
    /// Stops music whenever cassette player is removed.
    /// </summary>
    private void OnPlayerUnequipped(Entity<CassettePlayerComponent> ent, ref GotUnequippedEvent args)
        => StopAllAudio(ent);

    private void RemoveAudioFromEquipee(EntityUid entity)
    {
        var slots = _inventory.GetSlotEnumerator(entity);
        while (slots.MoveNext(out var slot))
        {
            if (!slot.ContainedEntity.HasValue)
                continue;

            if (!TryComp(slot.ContainedEntity.Value, out CassettePlayerComponent? player))
                continue;
            StopAllAudio((slot.ContainedEntity.Value, player));
        }
    }

    private void OnPlayerGetItemActions(Entity<CassettePlayerComponent> ent, ref GetItemActionsEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.InHands || args.SlotFlags is not { } slots)
            return;

        if (!ent.Comp.Slots.Any(flags => flags.HasFlag(slots)))
            return;

        // Player needs headphones on.
        if (!HasHeadphones(args.User))
        {
            _popup.PopupClient(Loc.GetString("null-cassette-headphones-missing"), ent, args.User);
            return;
        }

        args.AddAction(ref ent.Comp.PlayPauseAction, ent.Comp.PlayPauseActionId);
        args.AddAction(ref ent.Comp.NextAction, ent.Comp.NextActionId);
        args.AddAction(ref ent.Comp.RestartAction, ent.Comp.RestartActionId);
        Dirty(ent);
    }

    /// <returns>Whether the provided entity possesses headphones in its inventory.</returns>
    private bool HasHeadphones(EntityUid user)
    {
        // Look for Headphones
        var inventory = _inventory.GetSlotEnumerator(user);
        var hasHeadphones = false;
        while (inventory.MoveNext(out var slot))
        {
            if (!slot.ContainedEntity.HasValue)
                continue;
            hasHeadphones = TryComp(slot.ContainedEntity.Value, out HeadphonesComponent? _);
            if (hasHeadphones)
                break;
        }

        return hasHeadphones;
    }

    private void OnPlayerPlayPause(Entity<CassettePlayerComponent> ent, ref CassettePlayPauseActionEvent args)
    {
        var total = GetTotalSongs(ent);
        var tape = GetTape(ent);

        if (!tape.HasValue)
            return;

        var msg = Loc.GetString("null-cassette-headphones-missing");
        if (!HasHeadphones(args.Performer))
        {
            _popup.PopupClient(msg, ent, args.Performer);
            StopAllAudio(ent);
            return;
        }

        switch (ent.Comp.State)
        {
            case AudioState.Stopped:
            {
                PlaySong(ent, args.Performer);
                msg = Loc.GetString("rmc-cassette-playing",
                    ("player", ent),
                    ("name", GetCurrentSongName(ent)),
                    ("current", GetCurrentSongCount(ent)),
                    ("total", total));
                _popup.PopupClient(msg, ent, args.Performer);
                break;
            }
            case AudioState.Playing:
            {
                if (_net.IsServer)
                    _audio.SetState(ent.Comp.AudioStream, AudioState.Paused);
                else if (tape is { Comp.Custom: true })
                    _audio.SetState(ent.Comp.CustomAudioStream, AudioState.Paused);

                _popup.PopupClient(Loc.GetString("rmc-cassette-pause",
                        ("player", ent)),
                    ent,
                    args.Performer);
                ent.Comp.State = AudioState.Paused;
                break;
            }
            case AudioState.Paused:
            {
                if (_net.IsServer)
                    _audio.SetState(ent.Comp.AudioStream, AudioState.Playing);
                else if (tape is { Comp.Custom: true })
                    _audio.SetState(ent.Comp.CustomAudioStream, AudioState.Playing);

                msg = Loc.GetString("rmc-cassette-resume",
                    ("player", ent),
                    ("name", GetCurrentSongName(ent)),
                    ("current", GetCurrentSongCount(ent)),
                    ("total", total));
                _popup.PopupClient(msg, ent, args.Performer);
                ent.Comp.State = AudioState.Playing;
                break;
            }
        }

        _audio.PlayLocal(ent.Comp.PlayPauseSound, ent, args.Performer);
        Dirty(ent);
    }

    private void OnPlayerNext(Entity<CassettePlayerComponent> ent, ref CassetteNextActionEvent args)
    {
        var msg = Loc.GetString("null-cassette-headphones-missing");
        if (!HasHeadphones(args.Performer))
        {
            _popup.PopupClient(msg, ent, args.Performer);
            StopAllAudio(ent);
            return;
        }

        PlaySong(ent, args.Performer, ent.Comp.Tape + 1);
        msg = Loc.GetString("rmc-cassette-change",
            ("name", GetCurrentSongName(ent)),
            ("current", GetCurrentSongCount(ent)),
            ("total", GetTotalSongs(ent)));
        _popup.PopupClient(msg, ent, args.Performer);
        _audio.PlayLocal(ent.Comp.PlayPauseSound, ent, args.Performer);
    }

    private void OnPlayerRestart(Entity<CassettePlayerComponent> ent, ref CassetteRestartActionEvent args)
    {
        var msg = Loc.GetString("null-cassette-headphones-missing");
        if (!HasHeadphones(args.Performer))
        {
            _popup.PopupClient(msg, ent, args.Performer);
            StopAllAudio(ent);
            return;
        }
        PlaySong(ent, args.Performer);
        msg = Loc.GetString("rmc-cassette-restart",
            ("name", GetCurrentSongName(ent)),
            ("current", GetCurrentSongCount(ent)),
            ("total", GetTotalSongs(ent)));
        _popup.PopupClient(msg, ent, args.Performer);
        _audio.PlayLocal(ent.Comp.PlayPauseSound, ent, args.Performer);
    }

    private void PlaySong(Entity<CassettePlayerComponent> player, EntityUid actor, int? tape = null)
    {
        if (!_container.TryGetContainer(player, player.Comp.ContainerId, out var container) ||
            !container.ContainedEntities.TryFirstOrNull(out var tapeId) ||
            !TryComp(tapeId, out CassetteTapeComponent? tapeComp))
        {
            return;
        }

        StopAllAudio(player);

        tape ??= player.Comp.Tape;
        if (tape < 0 || tape >= tapeComp.Songs.Count)
            tape = 0;

        if (tapeComp.Custom)
        {
            if (PlayCustomTrack(player, (tapeId.Value, tapeComp)) is { } custom)
                player.Comp.CustomAudioStream = custom;

            player.Comp.Tape = 0;
        }
        else if (_net.IsServer)
        {
            var song = tapeComp.Songs[tape.Value];
            var audioParams = player.Comp.AudioParams;
            if (TryComp(actor, out ActorComponent? actorComp))
            {
                var gain = _netConfig.GetClientCVar(actorComp.PlayerSession.Channel, RMCCVars.VolumeGainCassettes);
                audioParams = audioParams.WithVolume(SharedAudioSystem.GainToVolume(gain));
            }

            player.Comp.AudioStream = _audio.PlayGlobal(song, actor, audioParams)?.Entity;
        }

        player.Comp.Tape = tape.Value;
        player.Comp.State = AudioState.Playing;
        Dirty(player);
        _item.VisualsChanged(player);
    }

    private void OnPlayerInteractUsing(Entity<CassettePlayerComponent> ent, ref InteractUsingEvent args)
    {
        if (!HasComp<CassetteTapeComponent>(args.Used))
            return;

        var slot = _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerId);
        var contained = slot.ContainedEntity;
        if (contained != null)
            _container.Remove(contained.Value, slot);

        _container.Insert(args.Used, slot);

        if (contained != null)
            _hands.TryPickupAnyHand(args.User, contained.Value);

        ent.Comp.Tape = 0;
        Dirty(ent);

        _audio.PlayLocal(ent.Comp.InsertEjectSound, ent, args.User);
    }

    // No RMC Eject item in hand.
    private void OnPlayerEjectHand(Entity<CassettePlayerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!_hands.IsHolding(args.User, ent))
            return;

        if (EjectTape(ent, args.User))
            args.Handled = true;
    }

    private void OnPlayerGetEquipmentVisuals(Entity<CassettePlayerComponent> ent, ref GetEquipmentVisualsEvent args)
    {
        args.Layers.Add(("cassette", new PrototypeLayerData
        {
            RsiPath = ent.Comp.WornSprite.RsiPath.ToString(),
            State = ent.Comp.WornSprite.RsiState,
        }));

        if (ent.Comp.State == AudioState.Playing)
        {
            args.Layers.Add(("cassette_music", new PrototypeLayerData
            {
                RsiPath = ent.Comp.MusicSprite.RsiPath.ToString(),
                State = ent.Comp.MusicSprite.RsiState,
            }));
        }
    }

    private void OnPlayerExamined(Entity<CassettePlayerComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(CassettePlayerComponent)))
        {
            var output = Loc.GetString("rmc-cassette-player-examine-none");
            if (TryGetTape(ent, out var tape))
                output = Loc.GetString("rmc-cassette-player-examine-tape", ("tape", tape));
            args.PushMarkup(output);
        }
    }

    private void OnPlayerState(Entity<CassettePlayerComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _item.VisualsChanged(ent);
    }

    private void OnPlayerRemovedFromContainer(Entity<CassettePlayerComponent> ent,
        ref EntRemovedFromContainerMessage args)
    {
        _audio.Stop(_net.IsServer ? ent.Comp.AudioStream : ent.Comp.CustomAudioStream);
        ent.Comp.State = AudioState.Stopped;
        ent.Comp.Tape = 0;
        Dirty(ent);
    }

    private void OnPlayerGetVerbsAlternative(Entity<CassettePlayerComponent> ent,
        ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("rmc-cassette-player-eject"),
            Act = () => EjectTape(ent, user),
        });
    }

    private void OnTapeExamined(Entity<CassetteTapeComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(CassetteTapeComponent)))
        {
            if (ent.Comp.Custom)
            {
                //args.PushMarkup(Loc.GetString("rmc-cassette-tape-custom"));
            }
            else
            {
                args.PushMarkup(Loc.GetString("rmc-cassette-tape-examine", ("total", ent.Comp.Songs.Count)));
            }
        }
    }

    // WIP: Add this to right-click menu for blank tapes.
    protected virtual void OnPlayerUseInHand(Entity<CassetteTapeComponent> tape, ref UseInHandEvent args)
    {
        if (!tape.Comp.Custom)
            return;

        args.Handled = true;
        ChooseCustomTrack(tape);
    }

    private void OnTapeGetVerbsAlternative(Entity<CassetteTapeComponent> tape, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!tape.Comp.Custom)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("rmc-cassette-tape-custom-choose"),
            Act = () =>
            {
                ChooseCustomTrack(tape);
            },
        });
    }

    private bool TryGetTape(Entity<CassettePlayerComponent> player, out Entity<CassetteTapeComponent> tape)
    {
        tape = default;
        if (!_container.TryGetContainer(player, player.Comp.ContainerId, out var container) ||
            !container.ContainedEntities.TryFirstOrNull(out var first) ||
            !TryComp(first, out CassetteTapeComponent? tapeComp))
        {
            return false;
        }

        tape = (first.Value, tapeComp);
        return true;
    }

    private Entity<CassetteTapeComponent>? GetTape(Entity<CassettePlayerComponent> player)
    {
        return TryGetTape(player, out var tape) ? tape : null;
    }

    private int GetCurrentSongCount(Entity<CassettePlayerComponent> player)
    {
        return player.Comp.Tape + 1;
    }

    private string GetCurrentSongName(Entity<CassettePlayerComponent> player)
    {
        var tape = GetTape(player);
        return !tape.HasValue ? "unknown" : tape.Value.Comp.Name;
    }

    private int GetTotalSongs(Entity<CassettePlayerComponent> player)
    {
        if (!TryGetTape(player, out var tape))
            return 0;

        var total = tape.Comp.Songs.Count;
        if (tape is { Comp.CustomTrack: not null })
            total++;

        return total;
    }

    protected virtual EntityUid? PlayCustomTrack(Entity<CassettePlayerComponent> player,
        Entity<CassetteTapeComponent> tape)
    {
        return null;
    }

    protected virtual void ChooseCustomTrack(Entity<CassetteTapeComponent> tape)
    {
    }

    private void StopAllAudio(Entity<CassettePlayerComponent> ent)
    {
        if (ent.Comp.AudioStream != null || ent.Comp.CustomAudioStream != null)
        {
            _audio.Stop(ent.Comp.AudioStream);
            _audio.Stop(ent.Comp.CustomAudioStream);
        }

        ent.Comp.State = AudioState.Stopped;
        Dirty(ent);

        _item.VisualsChanged(ent);
    }

    private bool EjectTape(Entity<CassettePlayerComponent> ent, EntityUid user)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container) ||
            !container.ContainedEntities.TryFirstOrNull(out var first) ||
            !_container.Remove(first.Value, container))
        {
            return false;
        }

        _hands.TryPickupAnyHand(user, first.Value);
        _audio.PlayLocal(ent.Comp.InsertEjectSound, ent, user);
        return true;
    }
}
