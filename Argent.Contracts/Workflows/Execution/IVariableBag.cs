namespace Argent.Contracts.Workflows.Execution;

public interface IVariableBag
{
    T? Get<T>(string key);
    object? Get(string key);
    void Set(string key, object? value);
    bool TryGetValue(string key, out object? value);
    IReadOnlyDictionary<string, object?> Snapshot();
}
