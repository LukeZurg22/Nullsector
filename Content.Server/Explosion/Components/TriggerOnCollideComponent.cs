namespace Content.Server.Explosion.Components
{
    [RegisterComponent]
    public sealed partial class TriggerOnCollideComponent : Component
    {
		[DataField("fixtureID", required: true)]
		public string FixtureID = string.Empty;

        /// <summary>
        ///     Doesn't trigger if the other colliding fixture is non-hard.
        /// </summary>
        [DataField("ignoreOtherNonHard")]
        public bool IgnoreOtherNonHard = true;
    }
}
