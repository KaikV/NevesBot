namespace KBot.App.Engine.Fusion
{
    public enum DataFreshness
    {
        Fresh,
        Stale,
        Unknown,
        Invalid
    }

    public sealed record FreshnessPolicy
    {
        public TimeSpan FreshMax { get; init; }
        public TimeSpan StaleMax { get; init; }

        public DataFreshness Evaluate(TimeSpan age)
        {
            if (age <= FreshMax) return DataFreshness.Fresh;
            if (age <= StaleMax) return DataFreshness.Stale;
            return DataFreshness.Invalid;
        }
    }

    public static class DefaultPolicies
    {
        public static readonly FreshnessPolicy Position = new() { FreshMax = TimeSpan.FromMilliseconds(250), StaleMax = TimeSpan.FromMilliseconds(750) };
        public static readonly FreshnessPolicy Creatures = new() { FreshMax = TimeSpan.FromMilliseconds(300), StaleMax = TimeSpan.FromMilliseconds(800) };
        public static readonly FreshnessPolicy Presence = new() { FreshMax = TimeSpan.FromSeconds(1), StaleMax = TimeSpan.FromSeconds(3) };
        public static readonly FreshnessPolicy Hp = new() { FreshMax = TimeSpan.FromMilliseconds(150), StaleMax = TimeSpan.FromMilliseconds(500) };
        public static readonly FreshnessPolicy Inventory = new() { FreshMax = TimeSpan.FromSeconds(3), StaleMax = TimeSpan.FromSeconds(10) };
    }
}
