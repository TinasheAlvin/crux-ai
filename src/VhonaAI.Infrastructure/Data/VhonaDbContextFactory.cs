using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VhonaAI.Infrastructure.Data;

public sealed class VhonaDbContextFactory : IDesignTimeDbContextFactory<VhonaDbContext>
{
    public VhonaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlite("Data Source=vhonaai.db")
            .Options;

        return new VhonaDbContext(options);
    }
}
