namespace CreatorRizz.Infrastructure.Configuration;

public sealed class ExternalServicesOptions
{
    public const string SectionName = "ExternalServices";
    public required string DatabaseConnectionString { get; init; }
    public required string RedisConnectionString { get; init; }
    public required string ObjectStorageEndpoint { get; init; }
}
