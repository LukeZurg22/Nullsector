using Content.Server._Null.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Null.Components;

[RegisterComponent, Access(typeof(ClaimantStakeSystem))]
public sealed partial class ClaimantStakeComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? PlayerOwner { get; set; }

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? ClaimedGrid { get; set; }

    [DataField("useSound")]
    public SoundSpecifier? SoundUse { get; set; }

    [DataField("awakeSound")]
    public SoundSpecifier? SoundAwake { get; set; }

    [DataField("sleepSound")]
    public SoundSpecifier? ShutdownSound { get; set; }

    public ClaimantStakeStatus StakeStatus = ClaimantStakeStatus.Offline;

    public const float AwaitBeepSoundTime = 5f;
    public float RemainingTime = AwaitBeepSoundTime;

    /// <summary>
    /// Check to see if this device is receiving power. Can be toggled, but it would be unwise due to possible
    /// unpredicted behaviour.
    /// </summary>
    public bool Enabled = false;

    #region Data Retention

    public IFFFlags OldFlags = IFFFlags.None;
    public string? OldColorHex = null;
    public string? OldGridName = null;
    public string? NewColorHex { get; set; }

    #endregion

    [ValidatePrototypeId<EntityPrototype>]
    public const string WreckagePrototype = "NFBaseWreckDebris";

    public const string WreckageRemovalQueueName = "SpaceDebris";
    public const string DefaultUseSound = "/Audio/Effects/metal_crunch.ogg";
    public const string DefaultAwakeSound = "/Audio/Effects/RingtoneNotes/asharp.ogg";
    public const string DefaultShutdownSound = "/Audio/Effects/sparks4.ogg";

    public static AudioParams DefaultAudioParameters = AudioParams.Default.WithVolume(-3);
}

public enum ClaimantStakeStatus : byte
{
    Offline,
    Warming,
    Online,
    Declaiming,
}
