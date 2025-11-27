using Content.Server.Power.Components;

namespace Content.Server.Power.EntitySystems;

public static class StaticPowerSystem
{
    // Using this makes the call shorter.
    // ReSharper disable once UnusedParameter.Global
    public static bool IsPowered(this EntitySystem system,
        EntityUid uid,
        IEntityManager entManager,
        ApcPowerReceiverComponent? receiver = null)
    {
        if (receiver == null && !entManager.TryGetComponent(uid, out receiver))
            return true;

        return receiver.Powered;
    }

    public static void Toggle<T>(this EntitySystem system, IEntityManager entManager, Entity<T> ent)
        where T : IComponent
    {
        if (IsPowered(system, ent.Owner, entManager))
            Disable(system, entManager, ent);
        else
            Enable(system, entManager, ent);
    }

    public static void Disable<T>(this EntitySystem system, IEntityManager entManager, Entity<T> ent) where T : IComponent
    {
        if (entManager.TryGetComponent<ApcPowerReceiverComponent>(ent, out var receiver))
            Disable(entManager, ent, receiver);
    }

    /// <summary>
    /// Forces power to be disabled.
    /// </summary>
    public static void Disable(IEntityManager entManager, EntityUid uid, ApcPowerReceiverComponent? receiver = null)
    {
        if (receiver == null && !entManager.TryGetComponent(uid, out receiver))
            return;
        receiver.PowerDisabled = true;
    }

    /// <summary>
    /// Lifts the enforced power disable, if there is any.
    /// </summary>
    public static void Enable(
        this EntitySystem system,
        IEntityManager entManager,
        EntityUid uid,
        ApcPowerReceiverComponent? receiver = null)
    {
        if (receiver == null && !entManager.TryGetComponent(uid, out receiver))
            return;
        receiver.PowerDisabled = false;
    }
}
