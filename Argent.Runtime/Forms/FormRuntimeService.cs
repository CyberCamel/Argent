using System.Text.Json;
using System.Globalization;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Forms;

public sealed partial class FormRuntimeService(
    IDbContextFactory<ArgentDbContext> dbFactory,
    IDomainObjectDefinitionService domainDefinitions,
    IDomainObjectStore domainObjects,
    IFormDataStore formData) : IFormRuntimeService
{
    public async Task<FormBootstrap?> BootstrapAsync(
        Guid formDesignId, Guid? recordId = null, CancellationToken cancellationToken = default)
        => await BootstrapAsync(formDesignId, new Dictionary<string, Guid>(), recordId, cancellationToken);

    public Task<FormBootstrap?> BootstrapAsync(
        Guid formDesignId, IReadOnlyDictionary<string, Guid> recordIds, CancellationToken cancellationToken = default)
        => BootstrapAsync(formDesignId, recordIds, null, cancellationToken);

    private async Task<FormBootstrap?> BootstrapAsync(
        Guid formDesignId, IReadOnlyDictionary<string, Guid> recordIds, Guid? recordId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var version = await db.FormDesignVersions.AsNoTracking()
            .Where(candidate => candidate.FormDesignId == formDesignId)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (version is null) return null;

        var compilation = FormDefinitionCompiler.Compile(version.Definition);
        if (!compilation.IsValid)
            throw new InvalidOperationException("The published form definition is invalid: " +
                string.Join("; ", compilation.Errors.Select(error => $"{error.Path}: {error.Message}")));

        if (version.Definition.Objects.Count > 0)
            return await BootstrapObjectsAsync(formDesignId, version, recordIds, recordId, cancellationToken);

        await HydrateReferenceOptionsAsync(version.Definition);

        var fieldsByName = FlattenFields(version.Definition.Components)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);
        var initialValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (recordId.HasValue)
        {
            var record = await domainObjects.GetAsync(version.Definition.ObjectKey, recordId.Value);
            if (record is null) return null;
            foreach (var item in record.Values)
                initialValues[item.Key] = ToProtocolElement(item.Key, item.Value, fieldsByName);
            var custom = await formData.GetCustomDataAsync(recordId.Value, formDesignId);
            foreach (var item in custom)
                initialValues[item.Key] = ToProtocolElement(item.Key, item.Value, fieldsByName);
        }

        return new FormBootstrap
        {
            FormDesignId = formDesignId,
            FormVersionId = version.Id,
            Definition = version.Definition,
            InitialValues = initialValues
        };
    }

    public async Task<FormRuntimeSubmission> SubmitAsync(
        Guid formDesignId,
        FormSubmitRequest request,
        string? user,
        CancellationToken cancellationToken = default,
        bool updateAttachedRecords = false)
    {
        if (request.ProtocolVersion != "2.0")
            return Invalid("protocolVersion", "protocol.unsupported", $"Unsupported protocol version '{request.ProtocolVersion}'.");
        if (request.SubmissionId == Guid.Empty)
            return Invalid("submissionId", "submission.id_required", "A submission identifier is required.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var prior = await db.FormSubmissionReceipts.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.SubmissionId, cancellationToken);
        if (prior is not null)
        {
            if (prior.FormDesignId != formDesignId || prior.FormVersionId != request.FormVersionId)
                return Invalid("submissionId", "submission.id_conflict", "The submission identifier has already been used for another form.");
            return new(new FormSubmitResult
            {
                RecordId = prior.RecordId,
                RecordIds = JsonSerializer.Deserialize<Dictionary<string, Guid>>(prior.RecordBindingsJson) ?? [],
                WorkflowInstanceId = prior.WorkflowInstanceId,
                IsReplay = true
            }, []);
        }
        var version = await db.FormDesignVersions.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.FormVersionId && candidate.FormDesignId == formDesignId, cancellationToken);
        if (version is null)
            return Invalid(string.Empty, "form.version_unknown", "The submitted form version does not exist.");

        var definitionCompilation = FormDefinitionCompiler.Compile(version.Definition);
        if (!definitionCompilation.IsValid)
            return new(null, definitionCompilation.Errors
                .Select(error => new FormValueError(string.Empty, error.Code, error.Message)).ToList());

        var fields = FlattenFields(version.Definition.Components).ToList();
        var knownKeys = fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        var unknownErrors = request.Values.Keys
            .Where(key => !knownKeys.Contains(key))
            .Select(key => new FormValueError(key, "value.field_unknown", "The submitted field is not part of this form."))
            .ToList();
        if (unknownErrors.Count > 0) return new(null, unknownErrors);

        var valueErrors = FormValueValidator.Validate(version.Definition, request.Values);
        if (valueErrors.Count > 0) return new(null, valueErrors);

        // Treat visibility and disabled state as a server-side data boundary, not merely UI state.
        // Values retained by a browser after a conditional field is hidden must not be persisted.
        var activeKeys = ActiveFieldKeys(version.Definition.Components, request.Values).ToHashSet(StringComparer.Ordinal);
        var activeValues = request.Values
            .Where(item => activeKeys.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        if (version.Definition.Objects.Count > 0)
            return await SubmitObjectsAsync(formDesignId, version, request, fields, activeValues, user,
                updateAttachedRecords, cancellationToken);

        var domainDefinition = await domainDefinitions.GetPublishedDefinitionAsync(version.Definition.ObjectKey);
        if (domainDefinition is null)
            return Invalid(string.Empty, "domain.definition_unknown", "The form's domain object is unavailable.");

        var referenceErrors = await ValidateReferencesAsync(fields, domainDefinition, request.Values);
        if (referenceErrors.Count > 0) return new(null, referenceErrors);

        var boundKeys = domainDefinition.Properties.Select(property => property.Key).ToHashSet(StringComparer.Ordinal);
        var boundValues = activeValues
            .Where(item => boundKeys.Contains(item.Key))
            .ToDictionary(item => item.Key, item => (object?)item.Value.Clone(), StringComparer.Ordinal);
        var customValues = activeValues
            .Where(item => !boundKeys.Contains(item.Key))
            .ToDictionary(item => item.Key, item => (object?)item.Value.Clone(), StringComparer.Ordinal);

        try
        {
            var record = request.RecordId.HasValue
                ? await domainObjects.UpdateAsync(version.Definition.ObjectKey, request.RecordId.Value, boundValues, user)
                : await domainObjects.CreateAsync(version.Definition.ObjectKey, boundValues, user);
            if (customValues.Count > 0)
                await formData.SaveCustomDataAsync(record.Id, formDesignId, customValues);
            if (!request.RecordId.HasValue)
            {
                db.FormSubmissionReceipts.Add(new FormSubmissionReceipt
                {
                    Id = request.SubmissionId,
                    FormDesignId = formDesignId,
                    FormVersionId = request.FormVersionId,
                    RecordId = record.Id
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            return new(new FormSubmitResult { RecordId = record.Id }, []);
        }
        catch (DomainValidationException exception)
        {
            return new(null, exception.Errors
                .Select(error => new FormValueError(error.Property, "domain.invalid", error.Message)).ToList());
        }
    }

    public async Task CompleteWorkflowStartAsync(
        Guid submissionId, Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var receipt = await db.FormSubmissionReceipts.FindAsync([submissionId], cancellationToken);
        if (receipt is null) throw new InvalidOperationException("The form submission receipt was not found.");
        receipt.WorkflowInstanceId = workflowInstanceId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DiscardSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.FormSubmissionReceipts.Where(candidate => candidate.Id == submissionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static FormRuntimeSubmission Invalid(string field, string code, string message) =>
        new(null, [new FormValueError(field, code, message)]);

    private static JsonElement ToJsonElement(object? value) =>
        value is JsonElement element ? element.Clone() : JsonSerializer.SerializeToElement(value);

    private static JsonElement ToProtocolElement(
        string key, object? value, IReadOnlyDictionary<string, FormField> fieldsByName)
    {
        if (!fieldsByName.TryGetValue(key, out var field) || value is null)
            return ToJsonElement(value);

        if (field.Type == "decimal")
        {
            var decimalText = value switch
            {
                JsonElement { ValueKind: JsonValueKind.Number } element => element.GetRawText(),
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                string text => text,
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => null
            };
            return decimalText is not null
                ? JsonSerializer.SerializeToElement(decimalText)
                : ToJsonElement(value);
        }

        if (field.Type != "date")
            return ToJsonElement(value);

        var date = value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateTimeOffset dateTimeOffset => DateOnly.FromDateTime(dateTimeOffset.DateTime),
            JsonElement { ValueKind: JsonValueKind.String } element => ParseDate(element.GetString()),
            string text => ParseDate(text),
            _ => null
        };

        return date.HasValue
            ? JsonSerializer.SerializeToElement(date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : ToJsonElement(value);
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dateOnly))
            return dateOnly;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var dateTimeOffset)
            ? DateOnly.FromDateTime(dateTimeOffset.DateTime)
            : null;
    }

    private async Task HydrateReferenceOptionsAsync(FormDefinition definition)
    {
        foreach (var field in FlattenFields(definition.Components).Where(field => field.Reference is not null))
        {
            var source = field.Reference!;
            var options = await domainObjects.GetOptionsAsync(source.ObjectKey, "id", source.LabelField);
            field.Options = options.Select(option => new FormOption
            {
                Value = Convert.ToString(option.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                Label = option.Label
            }).ToList();
        }
    }

    private async Task<List<FormValueError>> ValidateReferencesAsync(
        IEnumerable<FormField> fields,
        DomainObjectDefinition domainDefinition,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var properties = domainDefinition.Properties.ToDictionary(property => property.Key, StringComparer.Ordinal);
        var errors = new List<FormValueError>();
        foreach (var field in fields.Where(field => field.Reference is not null))
        {
            if (!values.TryGetValue(field.Name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (!properties.TryGetValue(field.PropertyKey ?? field.Name, out var property) || property.Type != DomainPropertyType.Reference ||
                property.IsCollection || property.ReferenceTargetKey != field.Reference!.ObjectKey)
            {
                errors.Add(new(field.Name, "reference.configuration_invalid", $"{field.Label} has an invalid reference configuration."));
                continue;
            }
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id) ||
                await domainObjects.GetAsync(field.Reference.ObjectKey, id) is null)
                errors.Add(new(field.Name, "reference.invalid", $"{field.Label} contains an unavailable reference."));
        }
        return errors;
    }

    private static IEnumerable<FormField> FlattenFields(IEnumerable<FormComponent> components)
    {
        foreach (var component in components)
        {
            if (component is FormField field) yield return field;
            if (component is FormLayout layout)
                foreach (var child in FlattenFields(layout.Children)) yield return child;
        }
    }

    private static IEnumerable<string> ActiveFieldKeys(
        IEnumerable<FormComponent> components,
        IReadOnlyDictionary<string, JsonElement> values,
        bool parentVisible = true)
    {
        foreach (var component in components)
        {
            if (component is FormField field)
            {
                var visible = parentVisible &&
                    (field.VisibleWhen is null || FormExpressionEvaluator.Evaluate(field.VisibleWhen, values));
                var disabled = field.DisabledWhen is not null &&
                    FormExpressionEvaluator.Evaluate(field.DisabledWhen, values);
                if (visible && !disabled) yield return field.Name;
            }
            else if (component is FormLayout layout)
            {
                var visible = parentVisible &&
                    (layout.VisibleWhen is null || FormExpressionEvaluator.Evaluate(layout.VisibleWhen, values));
                foreach (var key in ActiveFieldKeys(layout.Children, values, visible)) yield return key;
            }
        }
    }
}
