using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaskFlow.Infrastructure.Identity;

/// <summary>
/// Issues the tokens in invitation links. A separate provider so invitations can outlive password-reset tokens
/// (one day by default); like them, they're bound to the security stamp and stop working once a password is set.
/// </summary>
internal sealed class InvitationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "Invitation";
    public const string Purpose = "AcceptInvitation";
}

public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const int DefaultLifespanDays = 7;

    public InvitationTokenProviderOptions()
    {
        Name = InvitationTokenProvider.ProviderName;
        TokenLifespan = TimeSpan.FromDays(DefaultLifespanDays);
    }
}
