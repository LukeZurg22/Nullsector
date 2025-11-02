using System.Linq;
using Content.Server.Actions;
using Content.Server.Body.Systems;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Server.Emoting.Systems;
using Content.Server.Speech.EntitySystems;
using Content.Shared.Anomaly.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Cloning;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NameModifier.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Zombies;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Zombies;

public sealed partial class ZombieSystem : SharedZombieSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly AutoEmoteSystem _autoEmote = default!;
    [Dependency] private readonly EmoteOnDamageSystem _emoteOnDamage = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly NameModifierSystem _nameMod = default!;

    public const SlotFlags ProtectiveSlots =
        SlotFlags.FEET |
        SlotFlags.HEAD |
        SlotFlags.EYES |
        SlotFlags.GLOVES |
        SlotFlags.MASK |
        SlotFlags.NECK |
        SlotFlags.INNERCLOTHING |
        SlotFlags.OUTERCLOTHING;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZombieComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ZombieComponent, EmoteEvent>(OnEmote,
            before: [typeof(VocalSystem), typeof(BodyEmotesSystem)]);

        SubscribeLocalEvent<ZombieComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ZombieComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<ZombieComponent, CloningEvent>(OnZombieCloning);
        SubscribeLocalEvent<ZombieComponent, TryingToSleepEvent>(OnSleepAttempt);
        SubscribeLocalEvent<ZombieComponent, GetCharactedDeadIcEvent>(OnGetCharacterDeadIC);

        SubscribeLocalEvent<PendingZombieComponent, MapInitEvent>(OnPendingMapInit);
        SubscribeLocalEvent<PendingZombieComponent, BeforeRemoveAnomalyOnDeathEvent>(OnBeforeRemoveAnomalyOnDeath);

        SubscribeLocalEvent<IncurableZombieComponent, MapInitEvent>(OnPendingMapInit);

        SubscribeLocalEvent<ZombifyOnDeathComponent, MobStateChangedEvent>(OnDamageChanged);

        // TODO: Add OnCure event read, or whatever method of curing besides cloning comes up.
        //  Also a possible list of component *types* could be better, especially if using reflection to handle them.
    }

    private void OnBeforeRemoveAnomalyOnDeath(Entity<PendingZombieComponent> ent,
        ref BeforeRemoveAnomalyOnDeathEvent args)
    {
        // Pending zombies (e.g. infected non-zombies) do not remove their hosted anomaly on death.
        // Current zombies DO remove the anomaly on death.
        args.Cancelled = true;
    }

    private void OnPendingMapInit(EntityUid uid, IncurableZombieComponent component, MapInitEvent args)
    {
        _actions.AddAction(uid, ref component.Action, component.ZombifySelfActionPrototype);
    }

    private void OnPendingMapInit(EntityUid uid, PendingZombieComponent component, MapInitEvent args)
    {
        if (_mobState.IsDead(uid) || component.IsInstant)
        {
            ZombifyEntity(uid);
            return;
        }

        component.NextTick = _timing.CurTime + TimeSpan.FromSeconds(1f);
        component.GracePeriod = _random.Next(component.MinInitialInfectedGrace, component.MaxInitialInfectedGrace);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _timing.CurTime;

        // Hurt the living infected
        var query = EntityQueryEnumerator<PendingZombieComponent, DamageableComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damage, out var mobState))
        {
            // Process only once per second
            if (comp.NextTick > curTime)
                continue;

            comp.NextTick = curTime + TimeSpan.FromSeconds(1f);

            comp.GracePeriod -= TimeSpan.FromSeconds(1f);
            if (comp.GracePeriod > TimeSpan.Zero)
                continue;

            if (_random.Prob(comp.InfectionWarningChance))
                _popup.PopupEntity(Loc.GetString(_random.Pick(comp.InfectionWarnings)), uid, uid);

            var multiplier = _mobState.IsCritical(uid, mobState)
                ? comp.CritDamageMultiplier
                : 1f;

            _damageable.TryChangeDamage(uid, comp.Damage * multiplier, true, false, damage);
        }

        // Heal the zombies & Assess the Cured
        var zombieQuery = EntityQueryEnumerator<ZombieComponent, DamageableComponent, MobStateComponent>();
        while (zombieQuery.MoveNext(out var uid, out var comp, out var damage, out var mobState))
        {
            // Process only once per second
            if (comp.NextTick + TimeSpan.FromSeconds(1) > curTime)
                continue;

            comp.NextTick = curTime;

            if (_mobState.IsDead(uid, mobState))
                continue;

            var multiplier = _mobState.IsCritical(uid, mobState)
                ? comp.PassiveHealingCritMultiplier
                : 1f;

            // Gradual healing for living zombies.
            _damageable.TryChangeDamage(uid, comp.PassiveHealing * multiplier, true, false, damage);
        }

        // With this system, being cured / inoculated once permanently ensures one cannot be infected again.
        var curingQuery = EntityQueryEnumerator<ZombieComponent>();
        while (curingQuery.MoveNext(out var uid, out var comp))
        {
            if (!comp.CureInjected) // If cure is not injected,
                continue; // Short-Circuit
            UnZombifyEntity(uid, uid, comp, true);
            RemComp<ZombieComponent>(uid);
        }
    }

    private void OnSleepAttempt(EntityUid uid, ZombieComponent component, ref TryingToSleepEvent args)
    {
        args.Cancelled = true;
    }

    private void OnGetCharacterDeadIC(EntityUid uid, ZombieComponent component, ref GetCharactedDeadIcEvent args)
    {
        args.Dead = true;
    }

    private void OnStartup(EntityUid uid, ZombieComponent component, ComponentStartup args)
    {
        if (component.EmoteSoundsId == null)
            return;
        _protoManager.TryIndex(component.EmoteSoundsId, out component.EmoteSounds);

        GetOldJobId(uid, component);
    }

    private void GetOldJobId(EntityUid uid, ZombieComponent component)
    {
    }

    private void OnEmote(EntityUid uid, ZombieComponent component, ref EmoteEvent args)
    {
        // always play zombie emote sounds and ignore others
        if (args.Handled)
            return;
        args.Handled = _chat.TryPlayEmoteSound(uid, component.EmoteSounds, args.Emote);
    }

    private void OnMobState(EntityUid uid, ZombieComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
        {
            // Groaning when damaged
            EnsureComp<EmoteOnDamageComponent>(uid);
            _emoteOnDamage.AddEmote(uid, "Scream");

            // Random groaning
            EnsureComp<AutoEmoteComponent>(uid);
            _autoEmote.AddEmote(uid, "ZombieGroan");
        }
        else
        {
            // Stop groaning when damaged
            _emoteOnDamage.RemoveEmote(uid, "Scream");

            // Stop random groaning
            _autoEmote.RemoveEmote(uid, "ZombieGroan");
        }
    }

    private float GetZombieInfectionChance(EntityUid uid, ZombieComponent component)
    {
        var max = component.MaxZombieInfectionChance;

        if (!_inventory.TryGetContainerSlotEnumerator(uid, out var enumerator, ProtectiveSlots))
            return max;

        var items = 0f;
        var total = 0f;
        while (enumerator.MoveNext(out var con))
        {
            total++;
            if (con.ContainedEntity != null)
                items++;
        }

        if (total == 0)
            return max;

        // Everyone knows that when it comes to zombies, socks & sandals provide just as much protection as an
        // armored vest. Maybe these should be weighted per-item. I.e. some kind of coverage/protection component.
        // Or at the very least different weights per slot.

        var min = component.MinZombieInfectionChance;
        //gets a value between the max and min based on how many items the entity is wearing
        var chance = (max - min) * ((total - items) / total) + min;
        return chance;
    }

    private void OnMeleeHit(EntityUid uid, ZombieComponent component, MeleeHitEvent args)
    {
        if (!TryComp<ZombieComponent>(args.User, out _))
            return;

        if (!args.HitEntities.Any())
            return;

        foreach (var entity in args.HitEntities)
        {
            if (args.User == entity)
                continue;

            if (!TryComp<MobStateComponent>(entity, out var mobState))
                continue;

            if (HasComp<ZombieComponent>(entity))
            {
                args.BonusDamage = -args.BaseDamage;
            }
            else
            {
                if (!HasComp<ZombieImmuneComponent>(entity) && !HasComp<NonSpreaderZombieComponent>(args.User) &&
                    _random.Prob(GetZombieInfectionChance(entity, component)))
                {
                    EnsureComp<PendingZombieComponent>(entity);
                    EnsureComp<ZombifyOnDeathComponent>(entity);
                }
            }

            if (_mobState.IsIncapacitated(entity, mobState) && !HasComp<ZombieComponent>(entity) &&
                !HasComp<ZombieImmuneComponent>(entity))
            {
                ZombifyEntity(entity);
                args.BonusDamage = -args.BaseDamage;
            }
            else if (mobState.CurrentState == MobState.Alive) //heals when zombies bite live entities
            {
                _damageable.TryChangeDamage(uid, component.HealingOnBite, true, false);
            }
        }
    }

    private void OnZombieCloning(EntityUid uid, ZombieComponent zombieComponent, ref CloningEvent args)
    {
        if (UnZombifyEntity(args.Source, args.Target, zombieComponent))
        {
            args.NameHandled = true;
        }
    }
}
