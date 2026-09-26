using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` build the model without booting the API. Migrations are generated from the
/// model only, so the placeholder connection string is never used to reach a database.
/// </summary>
public sealed class CreatorRizzDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CreatorRizzDbContext>
{
    public CreatorRizzDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CreatorRizzDbContext>()
            .UseNpgsql("Host=localhost;Database=creatorrizz;Username=postgres;Password=postgres")
            .Options;
        return new CreatorRizzDbContext(options);
    }
}
