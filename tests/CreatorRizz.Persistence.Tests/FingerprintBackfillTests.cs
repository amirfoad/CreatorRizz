using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CreatorRizz.Persistence.Tests;

/// <summary>
/// Covers the fingerprint backfill, which is written twice: once in C# and once in SQL inside the
/// migration. If the two ever disagree, rows that predate the column stop taking part in headline
/// dedupe and the same story gets registered again under a second URL, silently. So the SQL is run
/// against the real server and compared with the domain function.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FingerprintBackfillTests(PostgresFixture fixture)
{
    private const string BackfillSql =
        """
        SELECT encode(sha256(convert_to(
            btrim(regexp_replace(lower(btrim(coalesce(@title, ''))), '[^[:alnum:]]+', ' ', 'g')) || '|' ||
            btrim(regexp_replace(lower(btrim(coalesce(@creator, ''))), '[^[:alnum:]]+', ' ', 'g')),
            'UTF8')), 'hex')
        """;

    public static TheoryData<string, string?> Titles => new()
    {
        { "Meteor Hits Coastal Town", "Example News" },
        { "  meteor   hits coastal town!  ", "example news" },
        { "Shark Sighted Near Pier, Again", null },
        { "شهاب سنگ به ساحل برخورد کرد", "اخبار نمونه" },
        { "Multi---word   /// headline", "A B C" },
        { "Single", "" },
        { "ALL CAPS HEADLINE", "ALL CAPS CREATOR" }
    };

    [Theory]
    [MemberData(nameof(Titles))]
    public async Task TheSqlBackfillProducesTheSameFingerprintAsTheDomain(string title, string? creator)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(BackfillSql, connection);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("creator", (object?)creator ?? DBNull.Value);
        var fromSql = (string)(await command.ExecuteScalarAsync())!;

        Assert.Equal(CandidateFingerprint.From(title, creator), fromSql);
    }

    [Fact]
    public async Task TheBackfillIsWhatMakesARegisteredStoryRepeatable()
    {
        var title = "A story that predates the fingerprint column";
        var candidate = new TopicCandidate
        {
            CanonicalUrl = $"https://example.com/{Guid.NewGuid():N}",
            Title = title,
            Creator = "Example News",
            Fingerprint = CandidateFingerprint.From(title, "Example News"),
            PublishedAt = DateTimeOffset.UtcNow,
            ViralScore = 50m
        };

        await using (var context = fixture.CreateContext())
        {
            context.TopicCandidates.Add(candidate);
            await context.SaveChangesAsync();
        }

        using var domain = fixture.CreateContext();
        var repository = new PostgresCandidateRepository(domain, ViralScoreWeights.Version1);
        var repeat = new DiscoveredTopic(
            "a story THAT predates the fingerprint COLUMN",
            $"https://other.example.com/{Guid.NewGuid():N}",
            "example news",
            DateTimeOffset.UtcNow,
            new ViralSignals(0m, 0m, 90m, 0m, 0m, 0m));

        Assert.Equal(candidate.Id, repository.RegisterDiscovered(repeat, ViralScoreWeights.Version1).Id);
        Assert.Equal(1, domain.TopicCandidates.Count(item => item.CanonicalUrl == candidate.CanonicalUrl));
        Assert.Equal(0, domain.TopicCandidates.Count(item => item.CanonicalUrl == repeat.CanonicalUrl));
    }
}
