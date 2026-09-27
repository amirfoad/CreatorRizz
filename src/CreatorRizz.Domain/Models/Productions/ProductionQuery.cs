namespace CreatorRizz.Domain;

/// <summary>
/// What an operator asked to see. Both filters are optional: with neither, this is the whole backlog;
/// with <see cref="AwaitingReview"/> it is the review queue. A page is bounded because an unbounded list
/// of productions is a query nobody can predict the cost of.
/// </summary>
public sealed record ProductionQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public ProductionQuery(ProductionState? state = null, ReviewKind? awaitingReview = null, int skip = 0, int take = DefaultPageSize)
    {
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip), "A page cannot start before the first one.");
        if (take is < 1 or > MaxPageSize) throw new ArgumentOutOfRangeException(nameof(take), $"A page must hold between 1 and {MaxPageSize} productions.");
        State = state;
        AwaitingReview = awaitingReview;
        Skip = skip;
        Take = take;
    }

    public ProductionState? State { get; }
    public ReviewKind? AwaitingReview { get; }
    public int Skip { get; }
    public int Take { get; }
}

/// <summary>
/// One page of productions and the number that matched the filter. A short page is the last page, so
/// <see cref="HasMore"/> needs the requested size: without it a partial page at the end of the results
/// would report a next page that does not exist, and a dashboard would show "next" forever.
/// </summary>
public sealed record ProductionPage(IReadOnlyCollection<Production> Productions, int TotalCount, int Skip, int Take)
{
    public bool HasMore => Productions.Count == Take && Skip + Productions.Count < TotalCount;
}
