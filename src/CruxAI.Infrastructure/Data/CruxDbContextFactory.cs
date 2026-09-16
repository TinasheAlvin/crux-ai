using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CruxAI.Infrastructure.Data;

public sealed class CruxDbContextFactory : IDesignTimeDbContextFactory<CruxDbContext>
{
    public CruxDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CruxDbContext>()
            .UseSqlite("Data Source=cruxai.db")
            .Options;

        return new CruxDbContext(options);
    }
}
