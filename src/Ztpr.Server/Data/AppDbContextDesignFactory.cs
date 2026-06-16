using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ztpr.Server.Data;

/// <summary>
/// Design-time factory used by the EF Core CLI (migrations) so it doesn't have to boot
/// the application host. The connection string here is only used for generating SQL.
/// </summary>
public sealed class AppDbContextDesignFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=ztpr-design.db")
            .Options;
        return new AppDbContext(options);
    }
}
