using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;

namespace CreatorRizz.Infrastructure.Scripting;

/// <summary>
/// The default until a managed model is configured. It refuses rather than returning composed text,
/// because a template is not source-grounded: a claim map built without a model would put uncited
/// commentary into the script review gate, which is the one failure this workflow is built to prevent.
/// </summary>
public sealed class DisabledScriptGenerator : IScriptGenerator
{
    public ValueTask<GeneratedScript> GenerateAsync(ScriptGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Summary)) throw new ArgumentException("A research pack summary is required.", nameof(request));
        if (request.Sources.Count == 0) throw new ArgumentException("At least one source is required.", nameof(request));
        throw new InvalidOperationException(
            "No script generator is configured. Add a managed model provider and its API key before drafting scripts from research.");
    }
}
