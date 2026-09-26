using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

/// <summary>What a generator is given. It carries the research pack summary and the sources to cite.</summary>
public sealed record ScriptGenerationRequest(
    Guid ProductionId,
    Guid CandidateId,
    string Title,
    string Summary,
    IReadOnlyCollection<SourceItem> Sources);

/// <summary>
/// A written script plus the provenance needed to audit it. <paramref name="ModelId"/> and
/// <paramref name="PromptVersion"/> are recorded rather than assumed, because a script that entered a
/// review gate has to be traceable to what produced it.
/// </summary>
public sealed record GeneratedScript(string Body, string ClaimMapJson, string ModelId, string PromptVersion);

/// <summary>Writes a source-grounded script. The only step in the workflow that needs a language model.</summary>
public interface IScriptGenerator
{
    ValueTask<GeneratedScript> GenerateAsync(ScriptGenerationRequest request, CancellationToken cancellationToken);
}
