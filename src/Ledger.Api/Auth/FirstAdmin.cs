using Ledger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Auth;

internal static class FirstAdmin
{
    public static async Task EnsureAsync(IServiceProvider services, IConfiguration configuration)
    {
        var userName = configuration["Ledger:FirstAdmin:UserName"];
        var password = configuration["Ledger:FirstAdmin:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return;
        }

        var db = services.GetRequiredService<LedgerDbContext>();
        if (await db.Users.AnyAsync())
        {
            return;
        }

        await services.GetRequiredService<UserDirectory>().CreateAsync(userName, password, [Roles.Admin], CancellationToken.None);
    }
}
