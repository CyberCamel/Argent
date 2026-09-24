using System.Data;
using System.Text.Json;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Core.Workflows.Auditing;
using Argent.Infrastructure.Data;
using Argent.Runtime.DomainObjects;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Forms;

public sealed partial class FormRuntimeService
{
    private async Task<FormBootstrap> BootstrapObjectsAsync(
        Guid formDesignId, FormDesignVersion version, IReadOnlyDictionary<string, Guid> recordIds,
        Guid? primaryRecordId, CancellationToken ct)
    {
        var definition = version.Definition;
        var ids = new Dictionary<string, Guid>(recordIds, StringComparer.Ordinal);
        var primary = definition.Objects.Single(binding => binding.IsPrimary);
        if (primaryRecordId.HasValue) ids.TryAdd(primary.Key, primaryRecordId.Value);

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var fields = FlattenFields(definition.Components).ToList();
        foreach (var binding in definition.Objects)
        {
            if (!ids.TryGetValue(binding.Key, out var id)) continue;
            var record = await domainObjects.GetAsync(binding.ObjectKey, id)
                ?? throw new InvalidOperationException($"Record for binding '{binding.Key}' no longer exists.");
            foreach (var field in fields.Where(field => field.ObjectBinding == binding.Key))
            {
                var property = field.PropertyKey ?? field.Name;
                if (record.Values.TryGetValue(property, out var value))
                    values[field.Name] = ToProtocolElement(field.Name, value,
                        new Dictionary<string, FormField> { [field.Name] = field });
            }
        }

        if (ids.TryGetValue(primary.Key, out var primaryId))
            foreach (var (key, value) in await formData.GetCustomDataAsync(primaryId, formDesignId))
                values[key] = ToJsonElement(value);

        await HydrateReferenceOptionsAsync(definition);
        return new FormBootstrap
        {
            FormDesignId = formDesignId,
            FormVersionId = version.Id,
            Definition = definition,
            InitialValues = values
        };
    }

    private async Task<FormRuntimeSubmission> SubmitObjectsAsync(
        Guid formDesignId, FormDesignVersion version, FormSubmitRequest request,
        IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, JsonElement> activeValues,
        string? user, bool updateAttachedRecords, CancellationToken ct)
    {
        var definition = version.Definition;
        var activeKeys = FormObjectActivation.ActiveBindings(definition, request.Values);
        var active = definition.Objects.Where(binding => activeKeys.Contains(binding.Key)).ToList();
        var primary = definition.Objects.Single(binding => binding.IsPrimary);
        var ids = new Dictionary<string, Guid>(request.RecordIds, StringComparer.Ordinal);
        if (request.RecordId.HasValue) ids.TryAdd(primary.Key, request.RecordId.Value);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var boundKeys = fields.Where(field => field.ObjectBinding is not null)
            .Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        var customValues = activeValues.Where(item => !boundKeys.Contains(item.Key))
            .ToDictionary(item => item.Key, item => (object?)item.Value.Clone(), StringComparer.Ordinal);

        try
        {
            // Children that assign their ID to a reference on another record are saved first.
            var pending = new List<FormObjectBinding>(active);
            while (pending.Count > 0)
            {
                var next = pending.FirstOrDefault(binding => !pending.Any(other =>
                    other.AssignToBinding == binding.Key));
                if (next is null)
                    return Invalid("objects", "object.assignment_cycle", "Object reference assignments form a cycle.");
                pending.Remove(next);

                var obj = await db.DomainObjects.AsNoTracking().FirstOrDefaultAsync(item => item.Key == next.ObjectKey, ct);
                if (obj is null)
                    return Invalid(next.Key, "domain.definition_unknown", $"Domain object '{next.ObjectKey}' is unavailable.");
                var published = await db.DomainObjectVersions.AsNoTracking()
                    .Where(item => item.DomainObjectId == obj.Id && item.State == DomainObjectState.Published)
                    .ToListAsync(ct);
                var objectVersion = published.OrderByDescending(item => item.Version).FirstOrDefault();
                var objectDefinition = objectVersion?.Definition;
                if (objectDefinition is null)
                    return Invalid(next.Key, "domain.definition_unknown", $"Domain object '{next.ObjectKey}' has no published definition.");

                var bindingFields = fields.Where(field => field.ObjectBinding == next.Key).ToList();
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var field in bindingFields)
                {
                    var propertyKey = field.PropertyKey ?? field.Name;
                    if (!objectDefinition.Properties.Any(property => property.Key == propertyKey))
                        return Invalid(field.Name, "domain.property_unknown", $"Property '{propertyKey}' is not on '{next.ObjectKey}'.");
                    if (active.Any(child => child.AssignToBinding == next.Key &&
                        child.AssignToProperty == propertyKey)) continue;
                    if (activeValues.TryGetValue(field.Name, out var value))
                        values[propertyKey] = value.Clone();
                }

                foreach (var child in active.Where(child => child.AssignToBinding == next.Key))
                    if (ids.TryGetValue(child.Key, out var childId))
                    {
                        var reference = objectDefinition.Properties.FirstOrDefault(property =>
                            property.Key == child.AssignToProperty);
                        if (reference is null || reference.Type != DomainPropertyType.Reference ||
                            reference.IsCollection || reference.ReferenceTargetKey != child.ObjectKey)
                            return Invalid(child.Key, "object.assignment_invalid",
                                $"'{child.AssignToProperty}' is not a reference to '{child.ObjectKey}' on '{next.ObjectKey}'.");
                        if (values.TryGetValue(child.AssignToProperty!, out var selected) && selected is not null &&
                            !string.IsNullOrWhiteSpace(selected.ToString()))
                            return Invalid(child.Key, "object.assignment_conflict",
                                "A reference cannot select an existing record and create a new one at the same time.");
                        values[child.AssignToProperty!] = childId;
                    }

                foreach (var field in bindingFields.Where(field => field.Reference is not null &&
                    !active.Any(child => child.AssignToBinding == next.Key &&
                        child.AssignToProperty == (field.PropertyKey ?? field.Name))))
                {
                    var errors = await ValidateReferencesAsync([field], objectDefinition, activeValues);
                    if (errors.Count > 0) return new(null, errors);
                }

                DomainObjectRecord row;
                var updating = updateAttachedRecords && ids.ContainsKey(next.Key);
                if (updating)
                {
                    if (!ids.TryGetValue(next.Key, out var id))
                        return Invalid(next.Key, "object.record_missing", $"No existing record is attached to '{next.Key}'.");
                    row = await db.DomainObjectRecords.FirstOrDefaultAsync(item => item.Id == id && item.DomainObjectId == obj.Id, ct)
                        ?? throw new InvalidOperationException($"Record for binding '{next.Key}' no longer exists.");
                    var merged = DomainValueCoercion.Coerce(row.Values, objectDefinition);
                    foreach (var (key, value) in values) merged[key] = value;
                    values = merged;
                }
                else if (ids.ContainsKey(next.Key))
                    return Invalid(next.Key, "object.already_created", $"A record is already attached to '{next.Key}'.");
                else
                {
                    row = new DomainObjectRecord
                    {
                        DomainObjectId = obj.Id,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = user ?? "Unknown"
                    };
                    db.DomainObjectRecords.Add(row);
                    ids[next.Key] = row.Id;
                }

                var coerced = DomainValueCoercion.Coerce(values, objectDefinition);
                var validation = DomainRecordValidator.Validate(objectDefinition, coerced);
                if (validation.Count > 0)
                    return new(null, validation.Select(error => new FormValueError(
                        bindingFields.FirstOrDefault(field => (field.PropertyKey ?? field.Name) == error.Property)?.Name
                            ?? $"{next.Key}.{error.Property}", "domain.invalid", error.Message)).ToList());

                var uniqueProperties = objectDefinition.Properties.Where(property => property.Unique &&
                    coerced.TryGetValue(property.Key, out var value) && value is not null);
                foreach (var property in uniqueProperties)
                {
                    bool duplicate;
                    if (db.Database.IsSqlServer())
                    {
                        var path = property.Key.Replace("\"", "\\\"").Replace("'", "''");
                        var query = $@"SELECT CASE WHEN EXISTS (
    SELECT 1 FROM DomainObjectRecords r WITH (UPDLOCK, SERIALIZABLE)
    WHERE r.DomainObjectId = {{0}} AND r.Id != {{1}}
      AND JSON_VALUE(r.[Values], '$.""{path}""') = {{2}}
) THEN 1 ELSE 0 END AS [Value]";
                        duplicate = await db.Database.SqlQueryRaw<int>(query, obj.Id, row.Id,
                            coerced[property.Key]?.ToString() ?? string.Empty).FirstOrDefaultAsync(ct) == 1;
                    }
                    else
                    {
                        var otherRows = await db.DomainObjectRecords.AsNoTracking()
                            .Where(item => item.DomainObjectId == obj.Id && item.Id != row.Id).ToListAsync(ct);
                        duplicate = otherRows.Any(other => other.Values.TryGetValue(property.Key, out var existing) &&
                            string.Equals(existing?.ToString(), coerced[property.Key]?.ToString(), StringComparison.Ordinal));
                    }
                    if (duplicate)
                        return Invalid($"{next.Key}.{property.Key}", "domain.unique", $"{property.DisplayName} must be unique.");
                }

                row.Values = coerced;
                row.DefinitionVersion = objectVersion!.Version;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = user ?? "Unknown";
                db.WorkflowJournalEntries.Add(new WorkflowJournalEntry
                {
                    Category = "DomainObject",
                    EventType = updating
                        ? nameof(WorkflowAuditEventType.DomainObjectUpdated)
                        : nameof(WorkflowAuditEventType.DomainObjectCreated),
                    Actor = user ?? "Unknown",
                    Details = JsonSerializer.Serialize(new { ObjectKey = next.ObjectKey, RecordId = row.Id })
                });
                await db.SaveChangesAsync(ct);
            }

            var primaryId = ids[primary.Key];
            if (customValues.Count > 0)
            {
                var custom = await db.FormCustomData.FirstOrDefaultAsync(item =>
                    item.RecordId == primaryId && item.FormId == formDesignId, ct);
                if (custom is null)
                    db.FormCustomData.Add(new FormCustomData { RecordId = primaryId, FormId = formDesignId, Values = customValues });
                else
                {
                    custom.Values = customValues;
                    custom.UpdatedAt = DateTime.UtcNow;
                }
            }

            db.FormSubmissionReceipts.Add(new FormSubmissionReceipt
            {
                Id = request.SubmissionId,
                FormDesignId = formDesignId,
                FormVersionId = version.Id,
                RecordId = primaryId,
                RecordBindingsJson = JsonSerializer.Serialize(ids)
            });
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return new(new FormSubmitResult { RecordId = primaryId, RecordIds = ids }, []);
        }
        catch (DomainValidationException exception)
        {
            return new(null, exception.Errors.Select(error =>
                new FormValueError(error.Property, "domain.invalid", error.Message)).ToList());
        }
    }
}
