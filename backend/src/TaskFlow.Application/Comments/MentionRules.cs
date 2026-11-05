using TaskFlow.Application.Users;

namespace TaskFlow.Application.Comments;

internal static class MentionRules
{
    /// <summary>The domain knows who is a member; whether the account is still active lives in Identity.</summary>
    public static async Task<bool> AllActiveAsync(
        IIdentityService identity, IReadOnlyCollection<Guid>? userIds, CancellationToken cancellationToken)
    {
        if (userIds is null || userIds.Count == 0)
        {
            return true;
        }

        var ids = userIds.Distinct().ToList();
        var users = await identity.FindByIdsAsync(ids, cancellationToken);
        return users.Count(user => user.IsActive && ids.Contains(user.Id)) == ids.Count;
    }
}
