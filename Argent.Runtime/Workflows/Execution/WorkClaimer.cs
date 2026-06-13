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
                SELECT TOP (@BatchSize) Id, TokenId, WorkflowInstanceId, NodeId, NodeType,
                       DefinitionId, RetryCount, MaxRetries, TokenPayload,
                       State, LockedBy, LockExpirationUtc
                FROM WorkItems WITH (ROWLOCK, READPAST)
                WHERE State = 0 AND (ScheduledAt IS NULL OR ScheduledAt <= GETUTCDATE())
                ORDER BY Priority DESC, CreatedAt
            )
            UPDATE claim_cte
            SET State = 1,
                LockedBy = @Machine,
                LockExpirationUtc = DATEADD(MINUTE, 5, GETUTCDATE())
            OUTPUT INSERTED.Id, INSERTED.TokenId, INSERTED.WorkflowInstanceId,
                   INSERTED.NodeId, INSERTED.NodeType, INSERTED.DefinitionId,
                   INSERTED.RetryCount, INSERTED.MaxRetries, INSERTED.TokenPayload;";

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
                InstanceId: reader.GetGuid(2),
                NodeId: reader.GetGuid(3),
                NodeType: reader.GetString(4),
                DefinitionId: reader.GetGuid(5),
                RetryCount: reader.GetByte(6),
                MaxRetries: reader.GetByte(7),
                TokenPayload: reader.IsDBNull(8) ? null : reader.GetString(8)
            ));
        }

        return results;
    }
}
