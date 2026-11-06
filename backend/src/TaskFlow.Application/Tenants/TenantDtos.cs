using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

/// <summary>A tenant the current user belongs to, with their role in it.</summary>
public sealed record MyTenantDto(Guid Id, string Name, string Slug, bool IsDemo, TenantRole Role);

/// <summary>A tenant as the super-admin sees every one.</summary>
public sealed record TenantDto(Guid Id, string Name, string Slug, bool IsDemo, DateTimeOffset CreatedAt);

public sealed record TenantMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    TenantRole Role,
    bool IsActive,
    bool IsInvitationPending,
    DateTimeOffset JoinedAt);

/// <param name="AccountCreated">Always true: the answer must not reveal whether the email already had an account in another tenant.</param>
/// <param name="InvitationSent">False when the email (invitation or notice) couldn't be sent; the membership exists anyway.</param>
public sealed record InvitedMemberDto(TenantMemberDto Member, bool AccountCreated, bool InvitationSent);

public sealed record SupportAccessDto(Guid Id, Guid UserId, string Email, string DisplayName, DateTimeOffset At);
