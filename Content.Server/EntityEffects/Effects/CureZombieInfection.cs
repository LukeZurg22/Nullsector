using Content.Server.Zombies;
using Content.Shared.EntityEffects;
using Content.Shared.Zombies;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class CureZombieInfection : EntityEffect
{
    [DataField("innoculate")]
    public bool Innoculate;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString(
            Innoculate
                ? "reagent-effect-guidebook-innoculate-zombie-infection"
                : "reagent-effect-guidebook-cure-zombie-infection",
                ("chance", Probability));
    }

    // Removes the Zombie Infection Components
    public override void Effect(EntityEffectBaseArgs args)
    {
        var entityManager = args.EntityManager;
        if (entityManager.HasComponent<IncurableZombieComponent>(args.TargetEntity))
            return;

        entityManager.RemoveComponent<ZombifyOnDeathComponent>(args.TargetEntity);
        entityManager.RemoveComponent<PendingZombieComponent>(args.TargetEntity);

        // TODO: Add Countdown to cure here or in Content.Server....ZombieSystem.cs

        // If cure is delivered, ensure they can never become zombies again.
        if (Innoculate)
            entityManager.EnsureComponent<ZombieImmuneComponent>(args.TargetEntity);

        // Flag CureInjected for Update() call in ZombieSystem.cs to begin de-zombification.
        if (entityManager.TryGetComponent(args.TargetEntity, out ZombieComponent? zombieComponent))
            zombieComponent.CureInjected = true;
    }
}
