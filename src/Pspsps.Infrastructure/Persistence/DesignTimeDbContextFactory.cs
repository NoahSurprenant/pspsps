using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pspsps.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without the server. It never connects.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PspspsDbContext>
{
    public PspspsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PspspsDbContext>()
            .UseNpgsql("Host=localhost;Database=pspsps")
            .UseSnakeCaseNamingConvention()
            .Options);
}
