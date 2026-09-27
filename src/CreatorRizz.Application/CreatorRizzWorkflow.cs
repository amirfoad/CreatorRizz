using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using System.Text.Json;

namespace CreatorRizz.Application;

public sealed class CreatorRizzWorkflow(
    ICandidateRepository candidates,
    IProductionRepository productions,
    IProductionJobQueue jobs,
    IScriptGenerator scriptGenerator,
    IWorkflowTransaction transactions,
    ViralScoreWeights viralScoreWeights)
{
    public IReadOnlyCollection<TopicCandidate> ListCandidates() => candidates.List();
    public TopicCandidate CreateCandidate(string url, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals) => candidates.Add(url, title, creator, publishedAt, signals);
    public TopicCandidate RegisterDiscovered(DiscoveredTopic topic) => candidates.RegisterDiscovered(topic, viralScoreWeights);
    public void AddSource(Guid candidateId, string url, string publisher, string? excerpt, int reliabilityScore) => candidates.AddSource(candidateId, url, publisher, excerpt, reliabilityScore);
    public IReadOnlyCollection<SourceItem> GetSources(Guid candidateId) => candidates.GetSources(candidateId);
    public bool CandidateExists(Guid candidateId) => candidates.TryGet(candidateId, out _);
    public ResearchPack CreateResearchPack(Guid candidateId, string summary, string factsJson, string uncertaintyJson) => candidates.CreateResearchPack(candidateId, summary, factsJson, uncertaintyJson);
    public bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack) => candidates.TryGetResearchPack(candidateId, out researchPack);

    /// <summary>
    /// Reads a discovery source and registers everything it found. Repeats are absorbed by the
    /// repository, so a retried discovery run neither fails nor duplicates a candidate.
    /// </summary>
    public async Task<IReadOnlyCollection<TopicCandidate>> DiscoverAsync(IDiscoverySource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var topics = await source.DiscoverAsync(cancellationToken);
        return topics.Select(topic => candidates.RegisterDiscovered(topic, viralScoreWeights)).ToArray();
    }

    /// <summary>
    /// Splits a candidate's sources into what they verify and what they leave open. A candidate without
    /// enough usable sources is refused here, so no research pack and therefore no production can be
    /// built on thin evidence. Returns null when a pack already exists, which keeps a retried job from
    /// overwriting reviewed research.
    /// </summary>
    public ResearchPack? BuildResearchPackFromSources(Guid candidateId)
    {
        if (candidates.TryGetResearchPack(candidateId, out _)) return null;
        if (!candidates.TryGet(candidateId, out var candidate) || candidate is null) throw new KeyNotFoundException("Candidate was not found.");
        var sources = candidates.GetSources(candidateId);
        ResearchPolicy.EnsureSourcesAreSufficient(sources);

        var facts = sources
            .Where(source => source.ReliabilityScore >= ResearchPolicy.MinimumReliabilityScore && !string.IsNullOrWhiteSpace(source.Excerpt))
            .Select(source => new { source.Id, source.Url, source.Publisher, Fact = source.Excerpt })
            .ToArray();
        var uncertainty = sources
            .Where(source => source.ReliabilityScore < ResearchPolicy.MinimumReliabilityScore || string.IsNullOrWhiteSpace(source.Excerpt))
            .Select(source => new
            {
                source.Id,
                source.Url,
                Reason = string.IsNullOrWhiteSpace(source.Excerpt) ? "no excerpt was captured" : $"reliability score {source.ReliabilityScore} is below {ResearchPolicy.MinimumReliabilityScore}"
            })
            .ToArray();

        var summary = $"{candidates.GetSources(candidateId).Count} sources back this story. {facts.Length} of them carry a usable fact.";
        return candidates.CreateResearchPack(candidateId, summary, JsonSerializer.Serialize(facts), JsonSerializer.Serialize(uncertainty));
    }

    public Production CreateProduction(Guid candidateId)
    {
        if (!candidates.TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        return productions.Create(candidateId);
    }

    /// <summary>
    /// Writes a script through the configured generator and records what produced it. A candidate
    /// without a research pack never reaches the generator, so no script can be written from sources
    /// that were never verified.
    /// </summary>
    public async Task<ScriptVersion> DraftScriptFromResearchAsync(Guid productionId, int expectedVersion, CancellationToken cancellationToken)
    {
        var production = GetProduction(productionId);
        if (!candidates.TryGet(production.TopicCandidateId, out var candidate) || candidate is null) throw new KeyNotFoundException("Candidate was not found.");
        if (!candidates.TryGetResearchPack(candidate.Id, out var researchPack) || researchPack is null)
            throw new WorkflowRuleViolation("Research pack is required before drafting a script.");

        var sources = candidates.GetSources(candidate.Id);
        ResearchPolicy.EnsureSourcesAreSufficient(sources);
        var generated = await scriptGenerator.GenerateAsync(
            new ScriptGenerationRequest(productionId, candidate.Id, candidate.Title, researchPack.Summary, sources), cancellationToken);
        return productions.AddGeneratedScript(productionId, expectedVersion, generated, sources);
    }

    public bool TryGetProduction(Guid productionId, out Production? production) => productions.TryGet(productionId, out production);

    /// <summary>
    /// Reads a page of the backlog, or of the review queue when the query names a review kind. The
    /// queue is the same query rather than a second repository method because it is the same rows: a
    /// production waiting on a decision is one whose state says so.
    /// </summary>
    public ProductionPage ListProductions(ProductionQuery query) => productions.List(query);
    public IReadOnlyCollection<Asset> GetAssets(Guid productionId) => productions.GetAssets(productionId);
    public IReadOnlyCollection<ScriptVersion> GetScripts(Guid productionId) => productions.GetScripts(productionId);
    public IReadOnlyCollection<ScriptGeneration> GetScriptGenerations(Guid productionId) => productions.GetScriptGenerations(productionId);
    public ScriptVersion AddScript(Guid productionId, int expectedVersion, string body, string claimMapJson) => productions.AddScript(productionId, expectedVersion, body, claimMapJson);
    public IReadOnlyCollection<AuditEvent> GetAuditEvents(Guid productionId) => productions.GetAuditEvents(productionId);
    public void AttachAsset(Guid productionId, int expectedVersion, StoredObject stored, string type, string? sourceUrl, RightsStatus rightsStatus, string narrativePurpose, string? licenseEvidence) =>
        productions.AttachAsset(productionId, expectedVersion, stored, type, sourceUrl, rightsStatus, AssetUsagePolicy.NormalizePurpose(narrativePurpose), licenseEvidence);
    public void BeginAssetPreparation(Guid productionId, int expectedVersion) => productions.BeginAssetPreparation(productionId, expectedVersion);
    public void DeclareAssetsReady(Guid productionId, int expectedVersion) => productions.DeclareAssetsReady(productionId, expectedVersion);
    public void CompleteRendering(Guid productionId, int expectedVersion) => productions.CompleteRendering(productionId, expectedVersion);
    public void SubmitReview(Guid productionId, int expectedVersion, ReviewKind kind) => productions.Submit(productionId, expectedVersion, kind);
    public void DecideReview(Guid productionId, int expectedVersion, ReviewKind kind, ReviewOutcome outcome, string reviewerId, string? notes) => productions.Decide(productionId, expectedVersion, kind, outcome, reviewerId, notes);

    public async ValueTask QueueTextToSpeechAsync(Guid productionId, string? voiceId, decimal speed, CancellationToken cancellationToken)
    {
        var production = GetProduction(productionId);
        if (production.State != ProductionState.ScriptApproved) throw new WorkflowRuleViolation("Script approval is required before TTS.");
        var script = productions.GetScripts(productionId).LastOrDefault() ?? throw new WorkflowRuleViolation("A script is required before TTS.");
        if (speed is < 0.5m or > 2m) throw new ArgumentOutOfRangeException(nameof(speed), "Speech speed must be between 0.5 and 2.0.");
        var effectiveVoiceId = string.IsNullOrWhiteSpace(voiceId) ? "alloy" : voiceId;
        var job = new TextToSpeechJob(productionId, script.Body, effectiveVoiceId, speed);
        await jobs.EnqueueTextToSpeechAsync(job, ProductionJobKey.ForTextToSpeech(productionId, production.Version), cancellationToken);
    }

    public async ValueTask QueueRenderAsync(Guid productionId, int expectedVersion, RenderManifest manifest, CancellationToken cancellationToken)
    {
        if (manifest.ProductionId != productionId) throw new WorkflowRuleViolation("Render manifest must reference the production being rendered.");
        // The state change and the record of the work it implies commit together. Committing the state
        // first leaves a crash in between with the production in Rendering and no queued work, and
        // nothing reconciles a production waiting for a render that was never recorded.
        await transactions.RunAsync(async token =>
        {
            productions.BeginRendering(productionId, expectedVersion, manifest);
            await jobs.EnqueueRenderAsync(manifest, ProductionJobKey.ForRender(productionId, expectedVersion), token);
            return true;
        }, cancellationToken);
    }

    private Production GetProduction(Guid productionId) => productions.TryGet(productionId, out var production) && production is not null
        ? production
        : throw new KeyNotFoundException("Production was not found.");
}
