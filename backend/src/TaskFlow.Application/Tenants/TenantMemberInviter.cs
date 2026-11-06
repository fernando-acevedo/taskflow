using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

/// <summary>
/// Adds an email to a tenant (ADR 0013). An existing account only gets the membership and a notice email; a new
/// one is created without a password and receives the invitation of ADR 0008. The membership is saved either way,
/// and a failed email is reported in the result instead of as an error.
/// The result never tells an existing account from a new one (see <see cref="InvitedMemberDto"/>).
/// </summary>
public sealed class TenantMemberInviter(
    IAppDbContext db,
    IIdentityService identity,
    IInvitationSender invitations,
    ITenantAccessCache accessCache,
    TimeProvider time)
{
    public async Task<Result<InvitedMemberDto>> InviteAsync(
        Guid tenantId,
        string tenantName,
        string email,
        string? displayName,
        TenantRole role,
        CancellationToken cancellationToken)
    {
        email = email.Trim();
        var user = await identity.FindByEmailAsync(email, cancellationToken);
        var created = user is null;

        if (user is not null)
        {
            var alreadyMember = await db.TenantMemberships.AnyAsync(
                membership => membership.TenantId == tenantId && membership.UserId == user.Id, cancellationToken);
            if (alreadyMember)
            {
                return TenantErrors.AlreadyMember;
            }
        }
        else
        {
            var account = await identity.CreateAsync(new NewUser(email, DisplayNameFor(email, displayName), false), cancellationToken);
            if (!account.IsSuccess)
            {
                return account.Error;
            }

            user = account.Value;
        }

        var membership = TenantMembership.Create(tenantId, user.Id, role, time.GetUtcNow());
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.Forget(user.Id, tenantId);

        bool sent;
        if (created)
        {
            var token = await identity.GenerateInvitationTokenAsync(user.Id, cancellationToken);
            sent = await invitations.SendAsync(user, token, cancellationToken);
        }
        else
        {
            sent = await invitations.SendTenantAddedAsync(user, tenantName, cancellationToken);
        }

        // The answer is the same whether or not the account existed: nothing about an account that lives outside this
        // tenant (name, state, whether it was just created) is revealed. The name shows in the member list afterwards.
        var member = new TenantMemberDto(
            user.Id, user.Email, DisplayNameFor(email, displayName), membership.Role, true, true, membership.JoinedAt);
        return new InvitedMemberDto(member, true, sent);
    }

    public static TenantMemberDto ToMember(UserDto user, TenantMembership membership) =>
        new(user.Id, user.Email, user.DisplayName, membership.Role, user.IsActive, user.IsInvitationPending, membership.JoinedAt);

    private static string DisplayNameFor(string email, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        var local = email[..email.IndexOf('@', StringComparison.Ordinal)];
        return local.Length >= 2 ? local : email;
    }
}
