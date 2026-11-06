using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Tenants;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.UnitTests.Tenants;

public sealed class TenantMemberHandlerTests
{
    private static readonly Tenant Acme = Tenant.Create("Acme", "acme", false, new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)).Value;
    private static readonly Guid TenantId = Acme.Id;
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IInvitationSender _invitations = Substitute.For<IInvitationSender>();
    private readonly ITenantAccessCache _cache = Substitute.For<ITenantAccessCache>();
    private readonly IDemoMode _demo = Substitute.For<IDemoMode>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly List<TenantMembership> _memberships = [];

    public TenantMemberHandlerTests()
    {
        As(TenantRole.Owner);
        var tenants = FakeDbSet.Of(Acme);
        _db.Tenants.Returns(tenants);
        Publish();
        _invitations.SendAsync(Arg.Any<UserDto>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _invitations.SendTenantAddedAsync(Arg.Any<UserDto>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private void As(TenantRole? role, Guid? tenantId = null)
    {
        _currentUser.Id.Returns(CallerId);
        _currentUser.TenantId.Returns(role is null ? null : tenantId ?? TenantId);
        _currentUser.TenantRole.Returns(role);
        _currentUser.IsTenantAdmin.Returns(role is TenantRole.Owner or TenantRole.Admin);
    }

    private TenantMembership Member(TenantRole role, Guid? userId = null)
    {
        var membership = TenantMembership.Create(TenantId, userId ?? Guid.NewGuid(), role, Now);
        _memberships.Add(membership);
        Publish();
        return membership;
    }

    private void Publish()
    {
        var set = FakeDbSet.Of([.. _memberships]);
        _db.TenantMemberships.Returns(set);
    }

    private TenantMemberInviter Inviter() => new(_db, _identity, _invitations, _cache, TimeProvider.System);

    private InviteTenantMemberHandler InviteHandler() =>
        new(_db, _currentUser, new InviteTenantMemberValidator(), Inviter(), _demo);

    private ChangeTenantMemberRoleHandler ChangeHandler() =>
        new(_db, _identity, _currentUser, new ChangeTenantMemberRoleValidator(), _cache, _demo);

    private RemoveTenantMemberHandler RemoveHandler() => new(_db, _identity, _currentUser, _cache, _demo);

    private static UserDto User(Guid id, string email, bool pending = false) =>
        new(id, email, "Someone", true, [], Now, pending);

    [Fact]
    public async Task InvitingAnExistingAccountAddsTheMembershipAndSendsOnlyANotice()
    {
        var userId = Guid.NewGuid();
        _identity.FindByEmailAsync("old@tests.local", Arg.Any<CancellationToken>()).Returns(User(userId, "old@tests.local"));

        var result = await InviteHandler().HandleAsync(new InviteTenantMemberCommand("old@tests.local", TenantRole.Member), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccountCreated.ShouldBeFalse();
        result.Value.InvitationSent.ShouldBeTrue();
        _db.TenantMemberships.Received(1).Add(Arg.Is<TenantMembership>(m => m.UserId == userId && m.TenantId == TenantId && m.Role == TenantRole.Member));
        _cache.Received(1).Forget(userId, TenantId);
        await _invitations.Received(1).SendTenantAddedAsync(Arg.Any<UserDto>(), "Acme", Arg.Any<CancellationToken>());
        await _identity.DidNotReceiveWithAnyArgs().CreateAsync(default!, TestContext.Current.CancellationToken);
        await _invitations.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InvitingANewEmailCreatesTheAccountAndSendsThePasswordInvitation()
    {
        var userId = Guid.NewGuid();
        _identity.CreateAsync(Arg.Any<NewUser>(), Arg.Any<CancellationToken>()).Returns(User(userId, "new@tests.local", pending: true));
        _identity.GenerateInvitationTokenAsync(userId, Arg.Any<CancellationToken>()).Returns("token");

        var result = await InviteHandler().HandleAsync(new InviteTenantMemberCommand(" new@tests.local ", TenantRole.Admin), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccountCreated.ShouldBeTrue();
        result.Value.Member.Role.ShouldBe(TenantRole.Admin);
        await _identity.Received(1).CreateAsync(Arg.Is<NewUser>(u => u.Email == "new@tests.local" && u.DisplayName == "new" && !u.IsAdmin), Arg.Any<CancellationToken>());
        await _invitations.Received(1).SendAsync(Arg.Any<UserDto>(), "token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvitingAnExistingMemberConflicts()
    {
        var userId = Guid.NewGuid();
        Member(TenantRole.Member, userId);
        _identity.FindByEmailAsync("old@tests.local", Arg.Any<CancellationToken>()).Returns(User(userId, "old@tests.local"));

        var result = await InviteHandler().HandleAsync(new InviteTenantMemberCommand("old@tests.local", TenantRole.Member), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TenantErrors.AlreadyMember);
        _db.TenantMemberships.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task AnAdminCannotInviteAnOwner()
    {
        As(TenantRole.Admin);

        var result = await InviteHandler().HandleAsync(new InviteTenantMemberCommand("x@tests.local", TenantRole.Owner), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TenantErrors.OwnerRequired);
    }

    [Theory]
    [InlineData(TenantRole.Member)]
    [InlineData(null)]
    public async Task OnlyTenantAdminsManageMembers(TenantRole? role)
    {
        As(role);

        var result = await InviteHandler().HandleAsync(new InviteTenantMemberCommand("x@tests.local", TenantRole.Member), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TenantErrors.Forbidden);
    }

    [Fact]
    public async Task InvitingIsDisabledInTheDemo()
    {
        _demo.IsEnabled.Returns(true);

        var result = await InviteHandler().HandleAsync(new InviteTenantMemberCommand("x@tests.local", TenantRole.Member), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(DemoErrors.ActionDisabled);
    }

    [Fact]
    public async Task ChangingARoleSavesAndForgetsTheCachedAccess()
    {
        var member = Member(TenantRole.Member);

        var result = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(member.UserId, TenantRole.Admin), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        member.Role.ShouldBe(TenantRole.Admin);
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.Received(1).Forget(member.UserId, TenantId);
    }

    [Fact]
    public async Task TheLastOwnerCannotBeDemotedOrRemoved()
    {
        var owner = Member(TenantRole.Owner);
        Member(TenantRole.Admin);

        var demote = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(owner.UserId, TenantRole.Admin), TestContext.Current.CancellationToken);
        var remove = await RemoveHandler().HandleAsync(new RemoveTenantMemberCommand(owner.UserId), TestContext.Current.CancellationToken);

        demote.Error.ShouldBe(TenantErrors.LastOwner);
        remove.Error.ShouldBe(TenantErrors.LastOwner);
        owner.Role.ShouldBe(TenantRole.Owner);
        _db.TenantMemberships.DidNotReceiveWithAnyArgs().Remove(default!);
        _cache.DidNotReceiveWithAnyArgs().Forget(default, default);
    }

    [Fact]
    public async Task AnOwnerCanBeDemotedWhenAnotherOwnerRemains()
    {
        var owner = Member(TenantRole.Owner);
        Member(TenantRole.Owner);

        var result = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(owner.UserId, TenantRole.Admin), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        owner.Role.ShouldBe(TenantRole.Admin);
    }

    [Fact]
    public async Task AnAdminCannotTouchOwners()
    {
        As(TenantRole.Admin);
        var owner = Member(TenantRole.Owner);
        Member(TenantRole.Owner);
        var member = Member(TenantRole.Member);

        var demote = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(owner.UserId, TenantRole.Member), TestContext.Current.CancellationToken);
        var promote = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(member.UserId, TenantRole.Owner), TestContext.Current.CancellationToken);
        var remove = await RemoveHandler().HandleAsync(new RemoveTenantMemberCommand(owner.UserId), TestContext.Current.CancellationToken);

        demote.Error.ShouldBe(TenantErrors.OwnerRequired);
        promote.Error.ShouldBe(TenantErrors.OwnerRequired);
        remove.Error.ShouldBe(TenantErrors.OwnerRequired);
    }

    [Fact]
    public async Task ChangingYourOwnRoleIsRejected()
    {
        Member(TenantRole.Owner, CallerId);

        var result = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(CallerId, TenantRole.Member), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(UserErrors.CannotChangeOwnRole);
    }

    [Fact]
    public async Task ChangingOrRemovingAStrangerIsNotFound()
    {
        var change = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(Guid.NewGuid(), TenantRole.Admin), TestContext.Current.CancellationToken);
        var remove = await RemoveHandler().HandleAsync(new RemoveTenantMemberCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        change.Error.ShouldBe(TenantErrors.MemberNotFound);
        remove.Error.ShouldBe(TenantErrors.MemberNotFound);
    }

    [Fact]
    public async Task RemovingAMemberDeletesTheMembershipAndForgetsTheCachedAccess()
    {
        var member = Member(TenantRole.Member);

        var result = await RemoveHandler().HandleAsync(new RemoveTenantMemberCommand(member.UserId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _db.TenantMemberships.Received(1).Remove(member);
        _cache.Received(1).Forget(member.UserId, TenantId);
    }

    [Fact]
    public async Task ProtectedDemoAccountsCannotBeDemotedOrRemoved()
    {
        var member = Member(TenantRole.Admin);
        _demo.IsEnabled.Returns(true);
        _demo.IsProtectedAccount("demo@tests.local").Returns(true);
        _identity.FindByIdAsync(member.UserId, Arg.Any<CancellationToken>()).Returns(User(member.UserId, "demo@tests.local"));

        var change = await ChangeHandler().HandleAsync(new ChangeTenantMemberRoleCommand(member.UserId, TenantRole.Member), TestContext.Current.CancellationToken);
        var remove = await RemoveHandler().HandleAsync(new RemoveTenantMemberCommand(member.UserId), TestContext.Current.CancellationToken);

        change.Error.ShouldBe(DemoErrors.AccountProtected);
        remove.Error.ShouldBe(DemoErrors.AccountProtected);
    }
}
