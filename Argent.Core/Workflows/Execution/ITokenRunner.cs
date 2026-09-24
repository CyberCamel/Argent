namespace Argent.Core.Workflows.Execution;

public interface ITokenRunner
{
    Task RunAsync(ClaimedWork claimed, CancellationToken ct);
}
