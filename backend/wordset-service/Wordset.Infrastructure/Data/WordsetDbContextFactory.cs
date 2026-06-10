using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wordset.Infrastructure.Data;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations add) at design time.
/// Run migrations from the solution root:
///   dotnet ef migrations add &lt;Name&gt; --project Wordset.Infrastructure --startup-project Wordset.API
/// </summary>
public class WordsetDbContextFactory : IDesignTimeDbContextFactory<WordsetDbContext>
{
    public WordsetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WordsetDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=wordsets;Username=vocabversus;Password=vocabversus")
            .Options;

        return new WordsetDbContext(options);
    }
}
