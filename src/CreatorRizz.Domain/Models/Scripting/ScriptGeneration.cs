namespace CreatorRizz.Domain;

/// <summary>
/// One attempt at writing a script, kept so the text that entered a review gate can be traced back to
/// the model, prompt version and exact sources it was given. <see cref="InputReferencesJson"/> holds
/// the source ids and URLs that were fed in, which is what makes a claim in the script checkable.
/// </summary>
public sealed class ScriptGeneration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionId { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public string InputReferencesJson { get; init; } = "[]";
    public string Body { get; init; } = string.Empty;
    public string ClaimMapJson { get; init; } = "{}";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
