using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VhonaAI.Infrastructure.Data;

public sealed class VhonaDbContextFactory : IDesignTimeDbContextFactory<VhonaDbContext>
{
    public VhonaDbContext CreateDbContext(string[] args)
    {
        // Design-time migrations target Azure SQL. The same migration set is applied to SQLite locally.
        var options = new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=vhonaai-design;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new VhonaDbContext(options);
    }
}
