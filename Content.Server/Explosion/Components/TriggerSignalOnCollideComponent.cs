using Content.Server.DeviceLinking.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.DeviceLinking;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.Explosion.Components;

[RegisterComponent, Access(typeof(SignalSwitchSystem), typeof(TriggerSystem))]
public sealed partial class TriggerSignalOnCollideComponent : Component
{
    [DataField("fixtureID", required: true)]
    public string FixtureID = string.Empty;

    /// <summary>
    ///     Doesn't trigger if the other colliding fixture is non-hard.
    /// </summary>
    [DataField("ignoreOtherNonHard")]
    public bool IgnoreOtherNonHard = true;

    /// <summary>
    ///     The port that gets signaled when the switch turns on.
    /// </summary>
    [DataField("onPort", customTypeSerializer: typeof(PrototypeIdSerializer<SourcePortPrototype>))]
    public string OnPort = "On";

    /// <summary>
    ///     The port that gets signaled when the switch turns off.
    /// </summary>
    [DataField("offPort", customTypeSerializer: typeof(PrototypeIdSerializer<SourcePortPrototype>))]
    public string OffPort = "Off";

    /// <summary>
    ///     The port that gets signaled with the switch's current status.
    ///     This is only used if OnPort is different from OffPort, not in the case of a toggle switch.
    /// </summary>
    [DataField("statusPort", customTypeSerializer: typeof(PrototypeIdSerializer<SourcePortPrototype>))]
    public string StatusPort = "Status";

    [DataField("state")]
    public bool State;

    [DataField("clickSound")]
    public SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Machines/lightswitch.ogg");
}
