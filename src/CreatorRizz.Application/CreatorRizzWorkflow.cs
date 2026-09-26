using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;

namespace CreatorRizz.Application;

public sealed class CreatorRizzWorkflow(
    ICandidateRepository candidates,
    IProductionRepository productions,
    IProductionJobQueue jobs)
{
    public IReadOnlyCollection<TopicCandidate> ListCandidates() => candidates.List();
    public TopicCandidate CreateCandidate(string url, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals) => candidates.Add(url, title, creator, publishedAt, signals);
    public void AddSource(Guid candidateId, string url, string publisher, string? excerpt, int reliabilityScore) => candidates.AddSource(candidateId, url, publisher, excerpt, reliabilityScore);
    public IReadOnlyCollection<SourceItem> GetSources(Guid candidateId) => candidates.GetSources(candidateId);
    public bool CandidateExists(Guid candidateId) => candidates.TryGet(candidateId, out _);
    public ResearchPack CreateResearchPack(Guid candidateId, string summary, string factsJson, string uncertaintyJson) => candidates.CreateResearchPack(candidateId, summary, factsJson, uncertaintyJson);
    public bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack) => candidates.TryGetResearchPack(candidateId, out researchPack);

    public Production CreateProduction(Guid candidateId)
    {
        if (!candidates.TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        return productions.Create(candidateId);
    }

    public ScriptVersion DraftScriptFromResearch(Guid productionId)
    {
        var production = GetProduction(productionId);
        if (!candidates.TryGet(production.TopicCandidateId, out var candidate) || candidate is null) throw new KeyNotFoundException("Candidate was not found.");
        if (!candidates.TryGetResearchPack(candidate.Id, out var researchPack) || researchPack is null)
            throw new WorkflowRuleViolation("Research pack is required before drafting a script.");

        var draft = ScriptDraftComposer.Compose(candidate, researchPack, candidates.GetSources(candidate.Id));
        return productions.AddScript(productionId, draft.Body, draft.ClaimMapJson);
    }

    public bool TryGetProduction(Guid productionId, out Production? production) => productions.TryGet(productionId, out production);
    public IReadOnlyCollection<Asset> GetAssets(Guid productionId) => productions.GetAssets(productionId);
    public IReadOnlyCollection<ScriptVersion> GetScripts(Guid productionId) => productions.GetScripts(productionId);
    public ScriptVersion AddScript(Guid productionId, string body, string claimMapJson) => productions.AddScript(productionId, body, claimMapJson);
    public IReadOnlyCollection<AuditEvent> GetAuditEvents(Guid productionId) => productions.GetAuditEvents(productionId);
    public void AttachAsset(Guid productionId, Asset asset) => productions.AttachAsset(productionId, asset);
    public void SubmitReview(Guid productionId, ReviewKind kind) => productions.Submit(productionId, kind);
    public void DecideReview(Guid productionId, ReviewKind kind, ReviewOutcome outcome, string reviewerId, string? notes) => productions.Decide(productionId, kind, outcome, reviewerId, notes);

    public async ValueTask QueueTextToSpeechAsync(Guid productionId, string? voiceId, decimal speed, CancellationToken cancellationToken)
    {
        var production = GetProduction(productionId);
        if (production.State != ProductionState.ScriptApproved) throw new WorkflowRuleViolation("Script approval is required before TTS.");
        var script = productions.GetScripts(productionId).LastOrDefault() ?? throw new WorkflowRuleViolation("A script is required before TTS.");
        if (speed is < 0.5m or > 2m) throw new ArgumentOutOfRangeException(nameof(speed), "Speech speed must be between 0.5 and 2.0.");
        var effectiveVoiceId = string.IsNullOrWhiteSpace(voiceId) ? "alloy" : voiceId;
        await jobs.EnqueueTextToSpeechAsync(new TextToSpeechJob(productionId, script.Body, effectiveVoiceId, speed), cancellationToken);
    }

    public async ValueTask QueueRenderAsync(Guid productionId, RenderManifest manifest, CancellationToken cancellationToken)
    {
        if (manifest.ProductionId != productionId) throw new WorkflowRuleViolation("Render manifest must reference the production being rendered.");
        productions.BeginRendering(productionId, manifest);
        await jobs.EnqueueRenderAsync(manifest, cancellationToken);
    }

    private Production GetProduction(Guid productionId) => productions.TryGet(productionId, out var production) && production is not null
        ? production
        : throw new KeyNotFoundException("Production was not found.");
}
