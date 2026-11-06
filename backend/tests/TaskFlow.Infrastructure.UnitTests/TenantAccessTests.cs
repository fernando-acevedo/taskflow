using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Infrastructure.UnitTests;

public sealed class TenantAccessTests
{
    [Fact]
    public void AMissingTenantIsNotFoundEvenWithoutRole() =>
        TenantAccess.ErrorFor(new TenantAccessResult(false, null)).ShouldBe(TenantErrors.NotFound);

    [Fact]
    public void AnExistingTenantWithoutMembershipIsForbidden() =>
        TenantAccess.ErrorFor(new TenantAccessResult(true, null)).ShouldBe(TenantErrors.Forbidden);

    [Theory]
    [InlineData(TenantRole.Member)]
    [InlineData(TenantRole.Admin)]
    [InlineData(TenantRole.Owner)]
    public void AMemberPasses(TenantRole role) =>
        TenantAccess.ErrorFor(new TenantAccessResult(true, role)).ShouldBeNull();

    [Fact]
    public void TheContextKeepsTenantAndRole()
    {
        var context = new TenantContext();
        var id = Guid.NewGuid();

        context.Use(id, TenantRole.Admin);

        context.TenantId.ShouldBe(id);
        context.Role.ShouldBe(TenantRole.Admin);
    }
}
