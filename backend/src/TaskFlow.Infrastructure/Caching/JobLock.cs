using Microsoft.Data.SqlClient;

namespace TaskFlow.Infrastructure.Caching;

/// <summary>
/// A lock between instances for background jobs, built on <c>sp_getapplock</c> so it needs no infrastructure beyond
/// the database (ADR 0020). The lock belongs to a dedicated connection (session owner) and is released when the
/// returned handle is disposed or the connection drops, so a crashed instance never holds it.
/// </summary>
public sealed class JobLock(string connectionString)
{
    /// <summary>Tries to take <paramref name="resource"/> without waiting; returns <c>null</c> when another instance holds it.</summary>
    public async Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", resource);
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", 0);
            var result = new SqlParameter("@ReturnValue", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.ReturnValue };
            command.Parameters.Add(result);
            await command.ExecuteNonQueryAsync(cancellationToken);

            // 0 and 1 mean granted; negative values mean timeout (held elsewhere), cancelled, deadlock or error.
            if ((int)result.Value >= 0)
            {
                return new Handle(connection, resource);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Handle(SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "sp_releaseapplock";
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@Resource", resource);
                command.Parameters.AddWithValue("@LockOwner", "Session");
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
#pragma warning disable CA1031 // Closing the connection releases a session lock anyway.
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            await connection.DisposeAsync();
        }
    }
}
