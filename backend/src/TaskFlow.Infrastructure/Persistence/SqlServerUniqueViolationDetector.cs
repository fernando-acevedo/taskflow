using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;

namespace TaskFlow.Infrastructure.Persistence;

internal sealed class SqlServerUniqueViolationDetector : IUniqueViolationDetector
{
    private const int DuplicateKeyIndex = 2601;
    private const int DuplicateKeyConstraint = 2627;

    public bool IsUniqueViolation(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.InnerException is SqlException { Number: DuplicateKeyIndex or DuplicateKeyConstraint };
    }
}
