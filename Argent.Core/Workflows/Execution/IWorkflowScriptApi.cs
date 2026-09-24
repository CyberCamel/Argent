namespace Argent.Core.Workflows.Execution;

public interface IWorkflowScriptApi
{
    object? GetInstanceValue(string name);

    void SetInstanceValue(string name, object? value);

    object? GetFormValue(string name);

    void SetFormValue(string name, object? value);

    IReadOnlyList<Dictionary<string, object?>> QueryDomainRecords(int take = 10);
}
