namespace CreatorRizz.Domain;

public sealed record ViralSignals(
    decimal ViewVelocity,
    decimal Engagement,
    decimal Recency,
    decimal CreatorRelevance,
    decimal CrossSourceSignal,
    decimal StoryPotential);

/// <summary>
/// Relative importance of each signal, expressed as a percentage of the final score. The weights
/// must total 100 so a misconfigured value cannot silently rescale every score into a range the
/// operator does not expect.
/// </summary>
public sealed record ViralScoreWeights(
    decimal ViewVelocity,
    decimal Engagement,
    decimal Recency,
    decimal CreatorRelevance,
    decimal CrossSource,
    decimal StoryPotential)
{
    public static ViralScoreWeights Version1 { get; } = new(35m, 20m, 15m, 10m, 10m, 10m);

    public decimal Total => ViewVelocity + Engagement + Recency + CreatorRelevance + CrossSource + StoryPotential;

    public void EnsureValid()
    {
        var values = new[] { ViewVelocity, Engagement, Recency, CreatorRelevance, CrossSource, StoryPotential };
        if (values.Any(value => value < 0))
            throw new ArgumentOutOfRangeException(nameof(ViralScoreWeights), "Viral score weights cannot be negative.");
        if (decimal.Abs(Total - 100m) > 0.001m)
            throw new ArgumentException($"Viral score weights must total 100 but they total {Total}.", nameof(ViralScoreWeights));
    }
}

public static class ViralScore
{
    public static decimal Calculate(ViralSignals signals) => Calculate(signals, ViralScoreWeights.Version1);

    public static decimal Calculate(ViralSignals signals, ViralScoreWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        weights.EnsureValid();
        var values = new[] { signals.ViewVelocity, signals.Engagement, signals.Recency, signals.CreatorRelevance, signals.CrossSourceSignal, signals.StoryPotential };
        if (values.Any(value => value is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(signals), "All viral signals must be in the 0 to 100 range.");

        return decimal.Round(
            signals.ViewVelocity * weights.ViewVelocity / 100m +
            signals.Engagement * weights.Engagement / 100m +
            signals.Recency * weights.Recency / 100m +
            signals.CreatorRelevance * weights.CreatorRelevance / 100m +
            signals.CrossSourceSignal * weights.CrossSource / 100m +
            signals.StoryPotential * weights.StoryPotential / 100m, 2);
    }
}
