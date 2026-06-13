using Argent.Contracts.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public class TokenVariableBag : IVariableBag
{
    private readonly Dictionary<string, object?> _variables;

    public TokenVariableBag()
    {
        _variables = new Dictionary<string, object?>();
    }

    public TokenVariableBag(Dictionary<string, object?> initial)
    {
        _variables = new Dictionary<string, object?>(initial);
    }

    public T? Get<T>(string key)
    {
        if (_variables.TryGetValue(key, out var value))
        {
            if (value is T typed)
                return typed;
            try { return (T?)Convert.ChangeType(value, typeof(T)); }
            catch { return default; }
        }
        return default;
    }

    public object? Get(string key)
    {
        return _variables.TryGetValue(key, out var value) ? value : null;
    }

    public void Set(string key, object? value)
    {
        _variables[key] = value;
    }

    public bool TryGetValue(string key, out object? value)
    {
        return _variables.TryGetValue(key, out value);
    }

    public IReadOnlyDictionary<string, object?> Snapshot()
    {
        return new Dictionary<string, object?>(_variables);
    }
}
