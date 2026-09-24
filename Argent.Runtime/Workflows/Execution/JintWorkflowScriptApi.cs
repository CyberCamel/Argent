using System.Text.Json;
using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;
using Argent.Core.Forms;
using Argent.Core.Workflows.Execution;

namespace Argent.Runtime.Workflows.Execution;

public sealed class JintWorkflowScriptApi(
    ITokenExecutionContext context,
    IDomainObjectStore? domainObjectStore,
    IFormDataStore? formDataStore,
    IDomainObjectDefinitionService? domainDefinitions) : IWorkflowScriptApi
{
    private readonly Dictionary<string, object?> _instanceUpdates = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> InstanceUpdates => _instanceUpdates;

    public object? GetInstanceValue(string name)
    {
        ValidateName(name);
        return context.Variables.Get(name);
    }

    public void SetInstanceValue(string name, object? value)
    {
        ValidateName(name);
        context.Variables.Set(name, value);
        _instanceUpdates[name] = value;
    }

    public object? GetFormValue(string name)
    {
        ValidateName(name);

        if (context.FormId.HasValue && formDataStore is not null)
        {
            var custom = RunSync(formDataStore.GetCustomDataAsync(context.RecordId, context.FormId.Value));
            if (custom.TryGetValue(name, out var value))
                return Normalize(value);
        }

        var record = GetCurrentRecord();
        return record is not null && record.Values.TryGetValue(name, out var recordValue)
            ? Normalize(recordValue)
            : null;
    }

    public void SetFormValue(string name, object? value)
    {
        ValidateName(name);

        var record = GetCurrentRecord();
        if (record is not null && domainDefinitions is not null && !string.IsNullOrWhiteSpace(context.ObjectKey))
        {
            var definition = RunSync(domainDefinitions.GetPublishedDefinitionAsync(context.ObjectKey));
            if (definition?.Properties.Any(property => property.Key == name) == true)
            {
                var values = new Dictionary<string, object?>(record.Values, StringComparer.Ordinal)
                {
                    [name] = value
                };
                RunSync(domainObjectStore!.UpdateAsync(context.ObjectKey, context.RecordId, values));
                context.Variables.Set(name, value);
                return;
            }
        }

        if (context.FormId.HasValue && formDataStore is not null)
        {
            var data = RunSync(formDataStore.GetCustomDataAsync(context.RecordId, context.FormId.Value));
            if (data.ContainsKey(name))
            {
                data[name] = value;
                RunSync(formDataStore.SaveCustomDataAsync(context.RecordId, context.FormId.Value, data));
                context.Variables.Set(name, value);
                return;
            }
        }

        throw new InvalidOperationException($"Form value '{name}' is not available for the current workflow context.");
    }

    public IReadOnlyList<Dictionary<string, object?>> QueryDomainRecords(int take = 10)
    {
        if (domainObjectStore is null || string.IsNullOrWhiteSpace(context.ObjectKey))
            return [];

        var result = RunSync(domainObjectStore.QueryAsync(context.ObjectKey, new DomainQuery
        {
            Take = Math.Clamp(take, 1, 25)
        }));

        return result.Records
            .Select(record => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = record.Id.ToString(),
                ["objectKey"] = record.ObjectKey,
                ["values"] = record.Values.ToDictionary(
                    pair => pair.Key,
                    pair => Normalize(pair.Value),
                    StringComparer.Ordinal)
            })
            .ToList();
    }

    private DomainRecord? GetCurrentRecord()
    {
        if (domainObjectStore is null || context.RecordId == Guid.Empty || string.IsNullOrWhiteSpace(context.ObjectKey))
            return null;

        return RunSync(domainObjectStore.GetAsync(context.ObjectKey, context.RecordId));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A value name is required.", nameof(name));
    }

    private static T RunSync<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static void RunSync(Task task) => task.GetAwaiter().GetResult();

    private static object? Normalize(object? value)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => element.EnumerateArray().Select(item => Normalize(item)).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => Normalize(property.Value),
                StringComparer.Ordinal),
            _ => element.ToString()
        };
    }
}
