using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Application.Abstractions;

/// <summary>Tells whether a failed save was caused by a unique index, without Application knowing the database provider.</summary>
public interface IUniqueViolationDetector
{
    bool IsUniqueViolation(DbUpdateException exception);
}
