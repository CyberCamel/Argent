using Argent.Contracts.Workflows.Execution;
using Microsoft.Data.SqlClient;

namespace Argent.Runtime.Workflows.Execution;

public class WorkClaimer : IWorkClaimer
{
    private readonly string _connectionString;

    public WorkClaimer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<ClaimedWork>> ClaimAsync(int batchSize, CancellationToken ct)
    {
        const string sql = @"
            WITH claim_cte AS (
                SELECT TOP (@BatchSize) Id, TokenId, NodeId, NodeType,
                       RetryCount, MaxRetries,
                       State, LockedBy, LockExpirationUtc
                FROM WorkItems WITH (ROWLOCK, READPAST)
                WHERE State = 0 AND (ScheduledAt IS NULL OR ScheduledAt <= GETUTCDATE())
                ORDER BY Priority DESC, CreatedAt
            )
            UPDATE claim_cte
            SET State = 1,
                LockedBy = @Machine,
                LockExpirationUtc = DATEADD(MINUTE, 5, GETUTCDATE())
            OUTPUT INSERTED.Id, INSERTED.TokenId,
                   INSERTED.NodeId, INSERTED.NodeType,
                   INSERTED.RetryCount, INSERTED.MaxRetries;";

        var results = new List<ClaimedWork>();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchSize", batchSize);
        cmd.Parameters.AddWithValue("@Machine", Environment.MachineName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ClaimedWork(
                WorkItemId: reader.GetGuid(0),
                TokenId: reader.GetGuid(1),
                NodeId: reader.GetGuid(2),
                NodeType: reader.GetString(3),
                RetryCount: reader.GetByte(4),
                MaxRetries: reader.GetByte(5)
            ));
        }

        return results;
    }
}
