using CreatorRizz.Application;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Persistence;
using CreatorRizz.Infrastructure.Scripting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace CreatorRizz.Persistence.Tests;

/// <summary>
/// Covers the discovery and research acceptance criteria against real PostgreSQL, using a fake
/// discovery source so the run never depends on a live feed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DiscoveryWorkflowTests(PostgresFixture fixture)
{
    private static readonly ViralSignals FeedSignals = new(0m, 0m, 90m, 0m, 0m, 0m);

    [Fact]
    public async Task ARepeatedDiscoveryRunRegistersEachStoryOnce()
    {
        var source = new FakeDiscoverySource(
        [
            Topic("Meteor Hits Coastal Town", "https://feed.example.com/meteor"),
            Topic("Second Story", "https://feed.example.com/second")
        ]);
        var workflow = CreateWorkflow();

        var first = await workflow.DiscoverAsync(source, CancellationToken.None);
        var second = await workflow.DiscoverAsync(source, CancellationToken.None);

        Assert.Equal(2, first.Count);
        Assert.Equal(first.Select(item => item.Id).Order(), second.Select(item => item.Id).Order());
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task ARetriedDiscoveryRunDoesNotFailOnTheStoriesItAlreadyKnows()
    {
        var workflow = CreateWorkflow();
        var source = new FakeDiscoverySource([Topic("Only Story", "https://feed.example.com/only")]);

        var first = await workflow.DiscoverAsync(source, CancellationToken.None);
        var repeat = await workflow.DiscoverAsync(source, CancellationToken.None);

        Assert.Equal(first.Single().Id, repeat.Single().Id);
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task ResearchRefusesToBuildAPackFromThinEvidence()
    {
        var workflow = CreateWorkflow();
        var candidate = (await workflow.DiscoverAsync(new FakeDiscoverySource([Topic("Thin Story", "https://feed.example.com/thin")]), CancellationToken.None)).Single();

        var failure = Assert.Throws<WorkflowRuleViolation>(() => workflow.BuildResearchPackFromSources(candidate.Id));
        Assert.Contains("at least two usable sources", failure.Message);
        Assert.False(workflow.TryGetResearchPack(candidate.Id, out _));
        Assert.Equal(0, CountResearchPacks());
    }

    [Fact]
    public async Task ResearchSeparatesVerifiedFactsFromWhatIsStillOpen()
    {
        var workflow = CreateWorkflow();
        var candidate = (await workflow.DiscoverAsync(new FakeDiscoverySource([Topic("Well Sourced Story", "https://feed.example.com/well")]), CancellationToken.None)).Single();
        workflow.AddSource(candidate.Id, "https://news.example.com/a", "News A", "The port reopened at dawn.", 80);
        workflow.AddSource(candidate.Id, "https://news.example.com/c", "News C", "The harbourmaster confirmed the reopening.", 90);
        workflow.AddSource(candidate.Id, "https://blog.example.com/b", "Blog B", "An unverified rumour.", 20);
        workflow.AddSource(candidate.Id, "https://news.example.com/d", "News D", null, 90);

        var pack = workflow.BuildResearchPackFromSources(candidate.Id);

        Assert.NotNull(pack);
        using var document = JsonDocument.Parse(pack!.FactsJson);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Contains("The port reopened at dawn.", pack.FactsJson);
        Assert.Contains("reliability score 20 is below 50", pack.UncertaintyJson);
        Assert.Contains("no excerpt was captured", pack.UncertaintyJson);
    }

    [Fact]
    public async Task ResearchIsIdempotentSoARetryKeepsTheReviewedPack()
    {
        var workflow = CreateWorkflow();
        var candidate = (await workflow.DiscoverAsync(new FakeDiscoverySource([Topic("Repeat Research", "https://feed.example.com/repeat")]), CancellationToken.None)).Single();
        workflow.AddSource(candidate.Id, "https://news.example.com/a", "News A", "First verified fact.", 80);
        workflow.AddSource(candidate.Id, "https://news.example.com/b", "News B", "Second verified fact.", 80);

        var first = workflow.BuildResearchPackFromSources(candidate.Id);
        var second = workflow.BuildResearchPackFromSources(candidate.Id);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1, CountResearchPacks());
    }

    [Fact]
    public async Task AGeneratedScriptIsRejectedBeforeTheGateWhenNoModelIsConfigured()
    {
        var workflow = CreateWorkflow(scriptGenerator: new DisabledScriptGenerator());
        var candidate = (await workflow.DiscoverAsync(new FakeDiscoverySource([Topic("Needs A Model", "https://feed.example.com/model")]), CancellationToken.None)).Single();
        workflow.AddSource(candidate.Id, "https://news.example.com/a", "News A", "First verified fact.", 80);
        workflow.AddSource(candidate.Id, "https://news.example.com/b", "News B", "Second verified fact.", 80);
        workflow.BuildResearchPackFromSources(candidate.Id);
        var production = workflow.CreateProduction(candidate.Id);
        var version = ReadVersion(production.Id);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.DraftScriptFromResearchAsync(production.Id, version, CancellationToken.None));

        Assert.Contains("No script generator is configured", failure.Message);
        Assert.Empty(workflow.GetScripts(production.Id));
        Assert.Empty(workflow.GetScriptGenerations(production.Id));
        Assert.True(workflow.TryGetProduction(production.Id, out var unchanged));
        Assert.Equal(ProductionState.ScriptDraft, unchanged!.State);
    }

    [Fact]
    public async Task AGeneratedScriptReachesTheGateWithItsProvenanceIntact()
    {
        var workflow = CreateWorkflow();
        var candidate = (await workflow.DiscoverAsync(new FakeDiscoverySource([Topic("Fake Model Story", "https://feed.example.com/fake")]), CancellationToken.None)).Single();
        workflow.AddSource(candidate.Id, "https://news.example.com/a", "News A", "First verified fact.", 80);
        workflow.AddSource(candidate.Id, "https://news.example.com/b", "News B", "Second verified fact.", 80);
        workflow.BuildResearchPackFromSources(candidate.Id);
        var production = workflow.CreateProduction(candidate.Id);
        var version = ReadVersion(production.Id);

        var script = await workflow.DraftScriptFromResearchAsync(production.Id, version, CancellationToken.None);
        var generation = Assert.Single(workflow.GetScriptGenerations(production.Id));

        Assert.Equal("fake-model-v1", generation.ModelId);
        Assert.Equal("prompt-v1", generation.PromptVersion);
        Assert.Equal(script.Body, generation.Body);
        Assert.Contains("https://news.example.com/a", generation.InputReferencesJson);
    }

    [Fact]
    public async Task AProductionWithoutAResearchPackNeverReachesTheGenerator()
    {
        var workflow = CreateWorkflow();
        var candidate = (await workflow.DiscoverAsync(new FakeDiscoverySource([Topic("No Research", "https://feed.example.com/noresearch")]), CancellationToken.None)).Single();
        workflow.AddSource(candidate.Id, "https://news.example.com/a", "News A", "First verified fact.", 80);
        workflow.AddSource(candidate.Id, "https://news.example.com/b", "News B", "Second verified fact.", 80);
        var production = workflow.CreateProduction(candidate.Id);
        var version = ReadVersion(production.Id);

        var failure = await Assert.ThrowsAsync<WorkflowRuleViolation>(() =>
            workflow.DraftScriptFromResearchAsync(production.Id, version, CancellationToken.None));

        Assert.Contains("Research pack is required", failure.Message);
    }

    private CreatorRizzWorkflow CreateWorkflow(IScriptGenerator? scriptGenerator = null) => new(
        new PostgresCandidateRepository(NewContext(), ViralScoreWeights.Version1),
        new PostgresProductionRepository(NewContext()),
        new ThrowingProductionJobQueue(),
        scriptGenerator ?? new FakeScriptGenerator(),
        ViralScoreWeights.Version1);

    private CreatorRizzDbContext NewContext() => fixture.CreateContext();

    private DiscoveredTopic Topic(string title, string url) =>
        new(title, $"{url}-{Guid.NewGuid():N}", "Example News", DateTimeOffset.UtcNow, FeedSignals);
    private int CountResearchPacks()
    {
        using var context = fixture.CreateContext();
        return context.ResearchPacks.Count();
    }

    private int ReadVersion(Guid production)
    {
        using var context = fixture.CreateContext();
        return context.Productions.AsNoTracking().Single(item => item.Id == production).Version;
    }

    /// <summary>Counts how often the fake was asked for topics, so a retry can be shown to really re-run.</summary>
    private sealed class FakeDiscoverySource(IReadOnlyCollection<DiscoveredTopic> topics) : IDiscoverySource
    {
        public string Name => "fake";
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyCollection<DiscoveredTopic>> DiscoverAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(topics);
        }
    }

    private sealed class FakeScriptGenerator : IScriptGenerator
    {
        public ValueTask<GeneratedScript> GenerateAsync(ScriptGenerationRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new GeneratedScript(
                $"Script for {request.Title} built from {request.Sources.Count} sources.",
                "{\"claim\":\"story\",\"sourceId\":\"fake\"}",
                "fake-model-v1",
                "prompt-v1"));
    }

    private sealed class ThrowingProductionJobQueue : IProductionJobQueue
    {
        public ValueTask EnqueueTextToSpeechAsync(TextToSpeechJob job, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask EnqueueRenderAsync(RenderManifest manifest, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
