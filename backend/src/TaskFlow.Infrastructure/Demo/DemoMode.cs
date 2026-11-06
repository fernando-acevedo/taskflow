using Microsoft.Extensions.Options;
using TaskFlow.Application.Demo;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Demo;

internal sealed class DemoMode(IOptions<DemoOptions> demo, IOptions<AdminSeedOptions> adminSeed) : IDemoMode
{
    public bool IsEnabled => demo.Value.Enabled;

    public bool IsProtectedAccount(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return string.Equals(email, adminSeed.Value.Email, StringComparison.OrdinalIgnoreCase)
            || demo.Value.Accounts.Exists(account => string.Equals(account.Email, email, StringComparison.OrdinalIgnoreCase));
    }
}
