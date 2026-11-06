using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Tenants;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.UnitTests.Tenants;

public sealed class TenantHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IInvitationSender _invitations = Substitute.For<IInvitationSender>();
    private readonly IDemoMode _demo = Substitute.For<IDemoMode>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public TenantHandlerTests()
    {
        _currentUser.Id.Returns(Guid.NewGuid());
        _currentUser.IsSuperAdmin.Returns(true);
        Use(FakeDbSet.Of<Tenant>(), FakeDbSet.Of<TenantMembership>());
        _invitations.SendAsync(Arg.Any<UserDto>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private void Use(DbSet<Tenant> tenants, DbSet<TenantMembership> memberships)
    {
        _db.Tenants.Returns(tenants);
        _db.TenantMemberships.Returns(memberships);
    }

    private CreateTenantHandler CreateHandler() => new(
        _db,
        _currentUser,
        new CreateTenantValidator(),
        new TenantMemberInviter(_db, _identity, _invitations, Substitute.For<ITenantAccessCache>(), TimeProvider.System),
        Substitute.For<IUniqueViolationDetector>(),
        _demo,
        TimeProvider.System);

    [Fact]
    public async Task OnlyTheSuperAdminCreatesTenants()
    {
        _currentUser.IsSuperAdmin.Returns(false);

        var result = await CreateHandler().HandleAsync(new CreateTenantCommand("Acme", "acme"), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("Tenants.SuperAdminRequired");
        _db.Tenants.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task RejectsAnInvalidSlug()
    {
        var result = await CreateHandler().HandleAsync(new CreateTenantCommand("Acme", "Not A Slug"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TenantErrors.InvalidSlug);
    }

    [Fact]
    public async Task RejectsATakenSlug()
    {
        Use(FakeDbSet.Of(Tenant.Create("Other", "acme", false, Now).Value), FakeDbSet.Of<TenantMembership>());

        var result = await CreateHandler().HandleAsync(new CreateTenantCommand("Acme", "acme"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(TenantErrors.SlugTaken);
        _db.Tenants.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task RejectsABadOwnerEmail()
    {
        var result = await CreateHandler().HandleAsync(new CreateTenantCommand("Acme", "acme", "nope"), TestContext.Current.CancellationToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.ValidationErrors!.ShouldContainKey("ownerEmail");
    }

    [Fact]
    public async Task CreatesTheTenantWithoutOwner()
    {
        var result = await CreateHandler().HandleAsync(new CreateTenantCommand(" Acme ", "acme"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Tenant.Name.ShouldBe("Acme");
        result.Value.Owner.ShouldBeNull();
        _db.Tenants.Received(1).Add(Arg.Is<Tenant>(tenant => tenant.Slug == "acme"));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesTheTenantWithAnOwnerMembership()
    {
        var userId = Guid.NewGuid();
        _identity.FindByEmailAsync("owner@tests.local", Arg.Any<CancellationToken>())
            .Returns(new UserDto(userId, "owner@tests.local", "Owner", true, [], Now));
        _invitations.SendTenantAddedAsync(Arg.Any<UserDto>(), "Acme", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().HandleAsync(new CreateTenantCommand("Acme", "acme", "owner@tests.local"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Owner!.AccountCreated.ShouldBeFalse();
        result.Value.Owner.Member.Role.ShouldBe(TenantRole.Owner);
        _db.TenantMemberships.Received(1).Add(Arg.Is<TenantMembership>(m => m.UserId == userId && m.Role == TenantRole.Owner));
    }

    [Fact]
    public async Task CreatingTenantsIsDisabledInTheDemo()
    {
        _demo.IsEnabled.Returns(true);

        var result = await CreateHandler().HandleAsync(new CreateTenantCommand("Acme", "acme"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(DemoErrors.ActionDisabled);
    }

    [Fact]
    public async Task ListsOnlyTheUsersOwnTenantsWithTheirRole()
    {
        var mine = Tenant.Create("Beta", "beta", false, Now).Value;
        var other = Tenant.Create("Alpha", "alpha", false, Now).Value;
        var me = _currentUser.Id;
        Use(
            FakeDbSet.Of(mine, other),
            FakeDbSet.Of(
                TenantMembership.Create(mine.Id, me, TenantRole.Admin, Now),
                TenantMembership.Create(other.Id, Guid.NewGuid(), TenantRole.Owner, Now)));

        var result = await new ListMyTenantsHandler(_db, _currentUser).HandleAsync(TestContext.Current.CancellationToken);

        var tenant = result.Value.ShouldHaveSingleItem();
        tenant.Id.ShouldBe(mine.Id);
        tenant.Role.ShouldBe(TenantRole.Admin);
    }

    [Fact]
    public async Task ListingAllTenantsIsForTheSuperAdmin()
    {
        _currentUser.IsSuperAdmin.Returns(false);

        var result = await new ListTenantsHandler(_db, _currentUser).HandleAsync(TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("Tenants.SuperAdminRequired");
    }

    [Fact]
    public async Task SupportAccessIsListedNewestFirstWithTheUserName()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _currentUser.TenantId.Returns(tenantId);
        _currentUser.TenantRole.Returns(TenantRole.Owner);
        _currentUser.IsTenantAdmin.Returns(true);
        var entries = FakeDbSet.Of(
            TenantSupportAccess.Create(tenantId, adminId, Now),
            TenantSupportAccess.Create(tenantId, adminId, Now.AddHours(1)),
            TenantSupportAccess.Create(Guid.NewGuid(), adminId, Now.AddHours(2)));
        _db.TenantSupportAccesses.Returns(entries);
        IReadOnlyList<UserDto> users = [new UserDto(adminId, "root@tests.local", "Root", true, ["Admin"], Now)];
        _identity.FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(users);

        var result = await new ListSupportAccessHandler(_db, _identity, _currentUser).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Count.ShouldBe(2);
        result.Value[0].At.ShouldBe(Now.AddHours(1));
        result.Value[0].Email.ShouldBe("root@tests.local");
    }
}
