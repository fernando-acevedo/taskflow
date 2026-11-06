using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Demo;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.ApiKeys;

/// <param name="ExpiresAt">Null means 90 days from now; at most one year.</param>
public sealed record CreateApiKeyCommand(string Name, ApiKeyScope Scope, DateTimeOffset? ExpiresAt);

public sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyCommand>
{
    public CreateApiKeyValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(ApiKey.NameMaxLength);
        RuleFor(command => command.Scope).IsInEnum();
    }
}

/// <summary>Any member creates keys for themselves in the current tenant (ADR 0016). Not available to keys or in the demo.</summary>
public sealed class CreateApiKeyHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDemoMode demo,
    IValidator<CreateApiKeyCommand> validator,
    ITransactionRunner transactions,
    TimeProvider time)
{
    public async Task<Result<CreatedApiKeyDto>> HandleAsync(CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.ApiKeyId is not null)
        {
            return ApiKeyErrors.NotAllowedWithKey;
        }

        if (demo.IsEnabled)
        {
            return DemoErrors.ActionDisabled;
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (currentUser.TenantId is null)
        {
            return TenantErrors.NotFound;
        }

        var now = time.GetUtcNow();
        var userId = currentUser.Id;
        CreatedApiKeyDto? dto = null;

        // Serializable: the count and the insert are atomic, so concurrent requests can't go over the limit.
        var result = await transactions.RunSerializableAsync(async token =>
        {
            var active = await db.ApiKeys.CountAsync(
                key => key.UserId == userId && key.RevokedAt == null && key.ExpiresAt > now, token);
            if (active >= ApiKey.MaxActivePerUser)
            {
                return ApiKeyErrors.TooMany;
            }

            var created = ApiKey.Create(userId, command.Name.Trim(), command.Scope, command.ExpiresAt, now);
            if (!created.IsSuccess)
            {
                return created.Error;
            }

            var (key, secret) = created.Value;
            db.ApiKeys.Add(key);
            await db.SaveChangesAsync(token);
            dto = new CreatedApiKeyDto(key.Id, key.Name, key.Prefix, key.Scope, key.CreatedAt, key.ExpiresAt, secret);
            return Result.Success();
        }, cancellationToken);

        return result.IsSuccess ? dto! : result.Error;
    }
}
