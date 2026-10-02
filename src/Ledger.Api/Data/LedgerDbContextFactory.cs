using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ledger.Api.Data;

public sealed class LedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LedgerDbContext>()
            .UseSqlServer("Server=localhost;Database=Ledger;Integrated Security=true")
            .Options);
}
