namespace GMS.Platform.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using GMS.Platform;

/// <summary>
/// Design-time factory so platform migrations can be generated without compiling GMS.Api.
/// Connection string is unused at generation time (the model is enough).
/// </summary>
public class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=HyMotionPlatformDesign;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable(
                    PlatformServiceExtensions.MigrationsHistoryTable,
                    PlatformServiceExtensions.Schema))
            .Options;
        return new PlatformDbContext(options);
    }
}
