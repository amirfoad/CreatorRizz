namespace CreatorRizz.Domain;

public sealed record ViralSignals(
    decimal ViewVelocity,
    decimal Engagement,
    decimal Recency,
    decimal CreatorRelevance,
    decimal CrossSourceSignal,
    decimal StoryPotential);

public static class ViralScore
{
    public static decimal Calculate(ViralSignals signals)
    {
        var values = new[] { signals.ViewVelocity, signals.Engagement, signals.Recency, signals.CreatorRelevance, signals.CrossSourceSignal, signals.StoryPotential };
        if (values.Any(x => x is < 0 or > 100)) throw new ArgumentOutOfRangeException(nameof(signals), "All viral signals must be in the 0 to 100 range.");
        return decimal.Round(
            signals.ViewVelocity * .35m + signals.Engagement * .20m + signals.Recency * .15m +
            signals.CreatorRelevance * .10m + signals.CrossSourceSignal * .10m + signals.StoryPotential * .10m, 2);
    }
}
