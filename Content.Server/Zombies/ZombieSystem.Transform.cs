using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Chat;
using Content.Server.Chat.Managers;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Humanoid;
using Content.Server.IdentityManagement;
using Content.Server.Inventory;
using Content.Server.Mind;
using Content.Server.Mind.Commands;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Speech.Components;
using Content.Server.Temperature.Components;
using Content.Shared.CombatMode;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost.Roles.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Prying.Components;
using Content.Shared.Roles;
using Content.Shared.Tag;
using Content.Shared.Traits.Assorted;
using Content.Shared.Weapons.Melee;
using Content.Shared.Zombies;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization.Manager;

namespace Content.Server.Zombies;

/// <summary>
///     Handles zombie propagation and inherent zombie traits
/// </summary>
/// <remarks>
///     Don't Shitcode Open Inside
/// </remarks>
public sealed partial class ZombieSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IChatManager _chatMan = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly NpcFactionSystem _faction = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidAppearance = default!;
    [Dependency] private readonly IdentitySystem _identity = default!;
    [Dependency] private readonly ServerInventorySystem _inventory = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly ISerializationManager _serializationManager = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly MetaDataSystem _metaSystem = default!;

    /// <summary>
    /// Handles an entity turning into a zombie when they die or go into crit
    /// </summary>
    private void OnDamageChanged(EntityUid uid, ZombifyOnDeathComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            ZombifyEntity(uid, args.Component);
        }
    }

    /// <summary>
    /// Only works for basic components whose inner values (if any) aren't important or specifically accessed.
    /// Effectively best for those components that can be added and removed without worry.
    /// </summary>
    private void DynamicRemoveComponents(EntityUid target, ZombieComponent zombieComponent)
    {
        // Goes over all removed components that are later re-added
        for (var i = 0; i < zombieComponent.BeforeComponentData.Count; i++)
        {
            var (_, componentType, _) = zombieComponent.BeforeComponentData[i];
            var hasComp = HasComp(target, zombieComponent.BeforeComponentData[i].Item2);

            if (!hasComp || !_entityManager.TryGetComponent(target, componentType, out var baseComponent))
                continue; // No component present? Short-Circuit.

            var newComponent = _serializationManager.CreateCopy(baseComponent, notNullableOverride: true);
            zombieComponent.BeforeComponentData[i] = (hasComp, componentType, (Component?)newComponent);
            RemComp(target, componentType); // Dynamic removal
        }
    }

    /// <summary>
    /// <inheritdoc cref="DynamicRemoveComponents"/>
    /// </summary>
    /// <param name="target"></param>
    /// <param name="zombieComponent"></param>
    private void DynamicAddComponents(EntityUid target, ZombieComponent zombieComponent)
    {
        // Goes over all removed components that are later re-added
        foreach (var (compPreviouslyExisted, componentType, oldComponent) in zombieComponent.BeforeComponentData)
        {
            if (!compPreviouslyExisted)
                continue; // Short-Circuit

            if (oldComponent == null)
                continue; // Short-Circuit

            // Full replacement, restoring old status.
            RemComp(target, componentType);

            var newComponent = _serializationManager.CreateCopy(oldComponent, notNullableOverride: true);
            AddComp(target, (Component)newComponent!);
        }
    }

    private const string Scream = "Scream";
    private const string ZombieGroan = "ZombieGroan";

    /// <summary>
    ///     This is the general purpose function to call if you want to zombify an entity.
    ///     It handles both humanoid and non-humanoid transformation and everything should be called through it.
    /// </summary>
    /// <param name="target">the entity being zombified</param>
    /// <param name="mobState"></param>
    /// <remarks>
    ///     ALRIGHT BIG BOYS, GIRLS AND ANYONE ELSE. YOU'VE COME TO THE LAYER OF THE BEAST. THIS IS YOUR WARNING.
    ///     This function is the god function for zombie stuff, and it is cursed. I have
    ///     attempted to label everything thoroughly for your sanity. I have attempted to
    ///     rewrite this, but this is how it shall lie eternal. Turn back now.
    ///     -emo
    /// </remarks>
    public void ZombifyEntity(EntityUid target, MobStateComponent? mobState = null)
    {
        //Don't zombfiy zombies
        if (HasComp<ZombieComponent>(target) || HasComp<ZombieImmuneComponent>(target))
            return;

        if (!Resolve(target, ref mobState, logMissing: false))
            return;

        // You're a real zombie now, son.
        var zombieComponent = AddComp<ZombieComponent>(target);
        zombieComponent.BeforeComponentData =
        [
            (false, typeof(RespiratorComponent), null),
            (false, typeof(BarotraumaComponent), null),
            (false, typeof(HungerComponent), null),
            (false, typeof(ThirstComponent), null),
            //(false, typeof(ReproductiveComponent), null), // Null Sector: Haha, animals can't have kids anymore! Oh-well!
            //(false, typeof(ReproductivePartnerComponent), null),
            (false, typeof(LegsParalyzedComponent), null),
            (false, typeof(ComplexInteractionComponent), null),
            (false, typeof(HandsComponent), null), // Bugged. Hands remain empty.
        ];

        // Store hand status. Copying the component is not enough, it seems.
        if (TryComp<HandsComponent>(target, out var handsComponent))
        {
            foreach (var hand in handsComponent.Hands)
            {
                zombieComponent.BeforeHands.Add(new ValueTuple<string, HandLocation>(hand.Value.Name, hand.Value.Location));
            }
        }

        // We need to basically remove all of these because zombies shouldn't
        //  get diseases, breath, be thirst, be hungry, die in space, have offspring or be paraplegic.
        DynamicRemoveComponents(target, zombieComponent);

        #region Handling Accents

        if (TryComp<ReplacementAccentComponent>(target, out var accentComp))
            zombieComponent.OldAccent = accentComp.Accent; // Store old accent, just in case.
        var accentType = "zombie";
        if (TryComp<ZombieAccentOverrideComponent>(target, out var accent))
            accentType = accent.Accent; // Assign new Zombino Accent
        EnsureComp<ReplacementAccentComponent>(target).Accent = accentType;

        #endregion

        //This is needed for stupid entities that fuck up combat mode component
        // in an attempt to make an entity not attack. This is the easiest way to do it.
        var combat = EnsureComp<CombatModeComponent>(target);
        RemComp<PacifiedComponent>(target);
        _combat.SetCanDisarm(target, false, combat);
        _combat.SetInCombatMode(target, true, combat);

        //This is the actual damage to the zombie. We assign the visual appearance
        //and range here because of stuff we'll find out later
        var melee = EnsureComp<MeleeWeaponComponent>(target);
        melee.Animation = zombieComponent.AttackAnimation;
        melee.WideAnimation = zombieComponent.AttackAnimation;
        melee.AltDisarm = false;
        melee.Range = 1.2f;
        melee.Angle = 0.0f;
        melee.HitSound = zombieComponent.BiteSound;

        if (mobState.CurrentState == MobState.Alive)
        {
            // Groaning when damaged
            EnsureComp<EmoteOnDamageComponent>(target);
            _emoteOnDamage.AddEmote(target, Scream);

            // Random groaning
            EnsureComp<AutoEmoteComponent>(target);
            _autoEmote.AddEmote(target, ZombieGroan);
        }

        #region Appearance and Attacks

        //We have specific stuff for humanoid zombies because they matter more
        if (TryComp<HumanoidAppearanceComponent>(target, out var huApComp)) //huapcomp
        {
            //store some values before changing them in case the humanoid get cloned later
            zombieComponent.BeforeZombifiedSkinColor = huApComp.SkinColor;
            zombieComponent.BeforeZombifiedEyeColor = huApComp.EyeColor;
            zombieComponent.BeforeZombifiedCustomBaseLayers = new Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo>(huApComp.CustomBaseLayers);
            if (TryComp<BloodstreamComponent>(target, out var stream))
                zombieComponent.BeforeZombifiedBloodReagent = stream.BloodReagent;

            _humanoidAppearance.SetSkinColor(target, zombieComponent.SkinColor, verify: false, humanoid: huApComp);

            // Messing with the eye layer made it vanish upon cloning, and also it didn't even appear right
            huApComp.EyeColor = zombieComponent.EyeColor;

            // this might not resync on clone?
            _humanoidAppearance.SetBaseLayerId(target,
                HumanoidVisualLayers.Tail,
                zombieComponent.BaseLayerExternal,
                humanoid: huApComp);
            _humanoidAppearance.SetBaseLayerId(target,
                HumanoidVisualLayers.HeadSide,
                zombieComponent.BaseLayerExternal,
                humanoid: huApComp);
            _humanoidAppearance.SetBaseLayerId(target,
                HumanoidVisualLayers.HeadTop,
                zombieComponent.BaseLayerExternal,
                humanoid: huApComp);
            _humanoidAppearance.SetBaseLayerId(target,
                HumanoidVisualLayers.Snout,
                zombieComponent.BaseLayerExternal,
                humanoid: huApComp);

            //This is done here because non-humanoids shouldn't get baller damage
            //lord forgive me for the hardcoded damage
            DamageSpecifier dspec = new()
            {
                DamageDict = new Dictionary<string, FixedPoint2>
                {
                    { "Slash", 13 },
                    { "Piercing", 7 },
                    { "Structural", 10 },
                },
            };
            melee.Damage = dspec;

            // humanoid zombies get to pry open doors and shit
            var pryComp = EnsureComp<PryingComponent>(target);
            pryComp.SpeedModifier = 0.75f;
            pryComp.PryPowered = true;
            pryComp.Force = true;

            Dirty(target, pryComp);
        }

        Dirty(target, melee);

        #endregion

        //The zombie gets the assigned damage weaknesses and strengths
        _damageable.SetDamageModifierSetId(target, "Zombie");

        #region Handling Blood & Bloodloss

        //This makes it so the zombie doesn't take bloodloss damage.
        //NOTE: they are supposed to bleed, just not take damage
        zombieComponent.BeforeBloodLossThreshold = _bloodstream.GetBloodLossThreshold(target);
        _bloodstream.SetBloodLossThreshold(target, 0f);
        _bloodstream.ChangeBloodReagent(target, zombieComponent.NewBloodReagent); // Give them zombie blood.

        #endregion

        //This is specifically here to combat insulated gloves, because frying zombies on grilles is funny as shit.
        _inventory.TryUnequip(target, "gloves", true, true);
        //Should prevent instances of zombies using comms for information they shouldn't be able to have.
            // Null Sector: Zombies will be permitted to hear comms, due to Semi-Intelligence vote.
            //_inventory.TryUnequip(target, "ears", true, true);

        // "Entity Has Turned Into A Zombie!" pop-up.
        _popup.PopupEntity(Loc.GetString("zombie-transform", ("target", target)), target, PopupType.LargeCaution);

        //Make it sentient if it's an animal or something
        MakeSentientCommand.MakeSentient(target, EntityManager);

        //Make the zombie not die in the cold. Good for space zombies
        if (TryComp<TemperatureComponent>(target, out var tempComp))
        {
            zombieComponent.BeforeColdDamage = _serializationManager.CreateCopy(tempComp.ColdDamage, notNullableOverride: true);
            tempComp.ColdDamage.ClampMax(0);
        }

        //Heals the zombie from all the damage it took while human
        if (TryComp<DamageableComponent>(target, out var damageableComponent))
            _damageable.SetAllDamage(target, damageableComponent, 0);
        _mobState.ChangeMobState(target, MobState.Alive);

        #region Handling Factions

        if (TryComp<NpcFactionMemberComponent>(target, out var npcFactionMemberComponent))
            zombieComponent.OldFactions = [..npcFactionMemberComponent.Factions]; // Store copy of old factions.
        _faction.ClearFactions(target, dirty: false);
        _faction.AddFaction(target, "Zombie");

        // Gives entity the "zombified ___" name prefix.
        _nameMod.RefreshNameModifiers(target);
        _identity.QueueIdentityUpdate(target);
        zombieComponent.CanRemoveZombieName = true; // Setting up to remove the "zombified" name, later.

        #endregion

        #region AI & Blackboarding

        var htn = EnsureComp<HTNComponent>(target);
        htn.RootTask = new HTNCompoundTask { Task = "SimpleHostileCompound" };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, target);
        _npc.SleepNPC(target, htn);

        // He's gotta have a mind
        var hasMind = _mind.TryGetMind(target, out var mindId, out _);
        if (hasMind && _mind.TryGetSession(mindId, out var session))
        {
            //Zombie role for player manifest
            _roles.MindAddRole(mindId, "MindRoleZombie", mind: null, silent: true);

            //Greeting message for new zombies
            _chatMan.DispatchServerMessage(session, Loc.GetString("zombie-infection-greeting"));

            // Notify player about new role assignment
            _audio.PlayGlobal(zombieComponent.GreetSoundNotification, session);
        }
        else
        {
            _npc.WakeNPC(target, htn);
        }

        #endregion

        // This specific component gives Build Test trouble, so I suppose let it do its thing.
        if (!HasComp<GhostRoleMobSpawnerComponent>(target) && !hasMind)
        {
            // Hardcoded Localization. Visit zombie.ftl for more information.
            var ghostRole = EnsureComp<GhostRoleComponent>(target);
            EnsureComp<GhostTakeoverAvailableComponent>(target);
            ghostRole.RoleName = Loc.GetString("zombie-generic");
            ghostRole.RoleDescription = Loc.GetString("zombie-role-desc");
            ghostRole.RoleRules = Loc.GetString("zombie-role-rules");
        }

        // Sloth: What the fuck?
        // LZ22: Wtf? Why? Is this magic? Witchcraft? I guess it stays here.
        // Placed here for esoteric reasons. Wait until Component Registry.
        if (HasComp<PullerComponent>(target))
        {
            zombieComponent.HadPullerComp = true;
            RemComp<PullerComponent>(target);
        }

        // No longer waiting to become a zombie:
        // Requires deferral because this is (probably) the event which called ZombifyEntity in the first place.
        RemCompDeferred<PendingZombieComponent>(target);

        // Zombie Gamemode Handling.
        var ev = new EntityZombifiedEvent(target);
        RaiseLocalEvent(target, ref ev, true);
        // Zombies get slowdown once they convert.
        _movementSpeedModifier.RefreshMovementSpeedModifiers(target);

        // Need to prevent them from getting an item, they have no hands.
        // Also prevents them from becoming a Survivor. They're undead.
        _tag.AddTag(target, "InvalidForGlobalSpawnSpell");
    }

    /// <summary>
    ///     This is the function to call if you want to de-zombify an entity.
    /// </summary>
    /// <param name="source">the entity having the ZombieComponent</param>
    /// <param name="target">the entity you want to de-zombify (different from source in case of cloning, for example)</param>
    /// <param name="zombieComponent"></param>
    /// <param name="isCure"></param>
    /// <remarks>
    ///     this currently only restore the name and skin/eye color from before zombified
    ///     TODO: completely rethink how zombies are done to allow reversal.
    /// </remarks>
    private bool UnZombifyEntity(EntityUid source, EntityUid target, ZombieComponent? zombieComponent, bool isCure = false)
    {
        if (!Resolve(source, ref zombieComponent))
            return false;

        foreach (var (layer, info) in zombieComponent.BeforeZombifiedCustomBaseLayers)
        {
            _humanoidAppearance.SetBaseLayerColor(target, layer, info.Color);
            _humanoidAppearance.SetBaseLayerId(target, layer, info.Id);
        }
        _humanoidAppearance.SetSkinColor(target, zombieComponent.BeforeZombifiedSkinColor);

        // Null Sector: The scars of Zombification run deep! - Keep zombified eyes.
        if (target != source)
            _humanoidAppearance.SetEyeColor(target, zombieComponent.BeforeZombifiedEyeColor);

        #region Handling Blood

        _bloodstream.SetBloodLossThreshold(target, zombieComponent.BeforeBloodLossThreshold);
        _bloodstream.ChangeBloodReagent(target, zombieComponent.BeforeZombifiedBloodReagent);

        #endregion

        // Undoing all previous actions.
        if (!isCure)
            return true; // Short-Circuit. Everything below is cleanup for those being cured whole-of-body.

        # region Handling Accents

        // Remove zombie accent
        RemComp<ZombieAccentOverrideComponent>(source);

        // Undoing accent changes, if any.
        if (!string.IsNullOrEmpty(zombieComponent.OldAccent))
        {
            if (TryComp<ReplacementAccentComponent>(source, out var accentComp))
                accentComp.Accent = zombieComponent.OldAccent;
            else
                RemComp<ReplacementAccentComponent>(source);
        }
        else
            RemComp<ReplacementAccentComponent>(source);

        #endregion

        #region Handling Factions

        _faction.ClearFactions(target, dirty: false);

        foreach (var faction in zombieComponent.OldFactions)
        {
            _faction.AddFaction(target, faction);
        }

        // Should automatically remove the "zombified ___" name prefix.
        _nameMod.RefreshNameModifiers(target);
        _identity.QueueIdentityUpdate(target);

        #endregion

        #region Handling Emotes

        _emoteOnDamage.RemoveEmote(target, Scream);
        _autoEmote.RemoveEmote(target, ZombieGroan);

        #endregion

        // Note: These are removed, regardless of whether someone had them previously or not. My reason for doing this
        // is two-fold: firstly I am tired and can't be bothered. Secondly, I like the idea that one's nails, claws, or
        // appendages have become to weak after being cured, that they can no longer use them as effectively.
        RemComp<MeleeWeaponComponent>(target); // Removes inherent melee ability.
        RemComp<PryingComponent>(target); // Removes ability to pry sealed doors open.
        // WARN: This also applies to fists! You can no longer punch after being cured as a zombie!

        // Restore Puller Component
        if (zombieComponent.HadPullerComp)
            EnsureComp<PullerComponent>(target);

        // Temperature restoration
        if (zombieComponent.BeforeColdDamage != null && TryComp<TemperatureComponent>(target, out var temperatureComponent))
            temperatureComponent.ColdDamage = _serializationManager.CreateCopy(zombieComponent.BeforeColdDamage, notNullableOverride: true);

        // Dynamically add components.
        DynamicAddComponents(target, zombieComponent);


        // Load old hand status. Copying the component is not enough.
        if (zombieComponent.BeforeHands.Count > 0)
        {
            EnsureComp<HandsComponent>(target, out var handsComponent);
            foreach (var hand in zombieComponent.BeforeHands)
            {
                // Item1 = Name, Item2 = Location
                _hands.AddHand(target, hand.Item1, hand.Item2, handsComponent);
            }
        }

        // Hopefully restore movement speed modifiers.
        _movementSpeedModifier.RefreshMovementSpeedModifiers(target);

        // Allows target to once again, gain an item.
        _tag.RemoveTag(target, "InvalidForGlobalSpawnSpell");
        zombieComponent.CureInjected = false;
        return true;
    }
}
