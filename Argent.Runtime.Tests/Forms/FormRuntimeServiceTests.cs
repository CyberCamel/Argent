using System.Text.Json;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Infrastructure.Data;
using Argent.Runtime.Forms;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public sealed class FormRuntimeServiceTests
{
    [Fact]
    public async Task Bootstrap_returns_latest_published_version()
    {
        var formId = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync(
            Version(formId, "Older", DateTime.UtcNow.AddMinutes(-1)),
            Version(formId, "Current", DateTime.UtcNow));

        var result = await fixture.Service.BootstrapAsync(formId);

        Assert.NotNull(result);
        Assert.Equal("Current", result.Definition.Title);
    }

    [Fact]
    public async Task Bootstrap_hydrates_reference_options_from_the_pinned_source()
    {
        var formId = Guid.NewGuid();
        var version = Version(formId, "Reference form", DateTime.UtcNow);
        version.Definition.Components = [ReferenceField()];
        await using var fixture = await Fixture.CreateAsync(version);
        var municipalityId = Guid.NewGuid();
        fixture.DomainObjects.Setup(store => store.GetOptionsAsync("municipality", "id", "name", null, null))
            .ReturnsAsync([new DomainOption { Value = municipalityId, Label = "Stockholm" }]);

        var result = await fixture.Service.BootstrapAsync(formId);

        var field = Assert.IsType<FormField>(Assert.Single(result!.Definition.Components));
        var option = Assert.Single(field.Options);
        Assert.Equal(municipalityId.ToString(), option.Value, ignoreCase: true);
        Assert.Equal("Stockholm", option.Label);
    }

    [Fact]
    public async Task Submit_rejects_a_reference_that_does_not_exist()
    {
        var formId = Guid.NewGuid();
        var version = Version(formId, "Reference form", DateTime.UtcNow);
        version.Definition.Components = [ReferenceField()];
        await using var fixture = await Fixture.CreateAsync(version);
        fixture.DomainDefinitions.Setup(service => service.GetPublishedDefinitionAsync("case"))
            .ReturnsAsync(new DomainObjectDefinition
            {
                Key = "case",
                Properties =
                [
                    new DomainProperty
                    {
                        Key = "municipality", Type = DomainPropertyType.Reference,
                        ReferenceTargetKey = "municipality"
                    }
                ]
            });
        var missingId = Guid.NewGuid();
        fixture.DomainObjects.Setup(store => store.GetAsync("municipality", missingId))
            .ReturnsAsync((DomainRecord?)null);

        var result = await fixture.Service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(),
            FormVersionId = version.Id,
            Values = new() { ["municipality"] = Json(missingId.ToString()) }
        }, null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Field == "municipality" && error.Code == "reference.invalid");
        fixture.DomainObjects.Verify(store => store.CreateAsync(
            It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Submit_rejects_values_not_declared_by_the_pinned_version()
    {
        var formId = Guid.NewGuid();
        var version = Version(formId, "Form", DateTime.UtcNow);
        await using var fixture = await Fixture.CreateAsync(version);

        var result = await fixture.Service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(),
            FormVersionId = version.Id,
            Values = new() { ["injected"] = Json("value") }
        }, null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Field == "injected" && error.Code == "value.field_unknown");
        fixture.DomainObjects.Verify(store => store.CreateAsync(
            It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Submit_does_not_persist_conditionally_hidden_values()
    {
        var formId = Guid.NewGuid();
        var version = Version(formId, "Form", DateTime.UtcNow);
        version.Definition.Components =
        [
            new FormField { Name = "name", Label = "Name" },
            new FormField { Name = "showSecret", Label = "Show secret", Type = "boolean" },
            new FormField
            {
                Name = "secret", Label = "Secret",
                VisibleWhen = new FormExpression
                {
                    Operator = "equals",
                    Left = new FormOperand { Field = "showSecret" },
                    Right = new FormOperand { Value = Json(true) }
                }
            }
        ];
        await using var fixture = await Fixture.CreateAsync(version);
        fixture.DomainDefinitions.Setup(service => service.GetPublishedDefinitionAsync("case"))
            .ReturnsAsync(new DomainObjectDefinition
            {
                Key = "case",
                Properties = [new DomainProperty { Key = "name", Type = DomainPropertyType.Text }]
            });
        var recordId = Guid.NewGuid();
        fixture.DomainObjects.Setup(store => store.CreateAsync("case", It.IsAny<IDictionary<string, object?>>(), "citizen"))
            .ReturnsAsync(new DomainRecord { Id = recordId, ObjectKey = "case" });

        var result = await fixture.Service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(),
            FormVersionId = version.Id,
            Values = new()
            {
                ["name"] = Json("Ada"),
                ["showSecret"] = Json(false),
                ["secret"] = Json("must not be stored")
            }
        }, "citizen");

        Assert.True(result.IsValid);
        fixture.FormData.Verify(store => store.SaveCustomDataAsync(recordId, formId,
            It.Is<Dictionary<string, object?>>(values =>
                values.ContainsKey("showSecret") && !values.ContainsKey("secret"))), Times.Once);
    }

    [Fact]
    public async Task Submit_replays_the_original_result_for_the_same_submission_id()
    {
        var formId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var version = Version(formId, "Form", DateTime.UtcNow);
        await using var fixture = await Fixture.CreateAsync(version);
        fixture.DomainDefinitions.Setup(service => service.GetPublishedDefinitionAsync("case"))
            .ReturnsAsync(new DomainObjectDefinition
            {
                Key = "case",
                Properties = [new DomainProperty { Key = "name", Type = DomainPropertyType.Text }]
            });
        fixture.DomainObjects.Setup(store => store.CreateAsync(
                "case", It.IsAny<IDictionary<string, object?>>(), "citizen"))
            .ReturnsAsync(new DomainRecord { Id = recordId, ObjectKey = "case" });
        var request = new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(),
            FormVersionId = version.Id,
            Values = new() { ["name"] = Json("Ada") }
        };

        var first = await fixture.Service.SubmitAsync(formId, request, "citizen");
        var retry = await fixture.Service.SubmitAsync(formId, request, "citizen");

        Assert.True(first.IsValid);
        Assert.True(retry.IsValid);
        Assert.False(first.Result!.IsReplay);
        Assert.True(retry.Result!.IsReplay);
        Assert.Equal(recordId, retry.Result.RecordId);
        fixture.DomainObjects.Verify(store => store.CreateAsync(
            "case", It.IsAny<IDictionary<string, object?>>(), "citizen"), Times.Once);
    }

    [Fact]
    public async Task Existing_record_bootstrap_and_submit_use_the_pinned_domain_record()
    {
        var formId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var version = Version(formId, "Task form", DateTime.UtcNow);
        await using var fixture = await Fixture.CreateAsync(version);
        fixture.DomainDefinitions.Setup(service => service.GetPublishedDefinitionAsync("case"))
            .ReturnsAsync(new DomainObjectDefinition
            {
                Key = "case",
                Properties = [new DomainProperty { Key = "name", Type = DomainPropertyType.Text }]
            });
        fixture.DomainObjects.Setup(store => store.GetAsync("case", recordId))
            .ReturnsAsync(new DomainRecord
            {
                Id = recordId,
                ObjectKey = "case",
                Values = new() { ["name"] = "Before" }
            });
        fixture.FormData.Setup(store => store.GetCustomDataAsync(recordId, formId))
            .ReturnsAsync([]);
        fixture.DomainObjects.Setup(store => store.UpdateAsync(
                "case", recordId, It.IsAny<IDictionary<string, object?>>(), "worker"))
            .ReturnsAsync(new DomainRecord { Id = recordId, ObjectKey = "case" });

        var bootstrap = await fixture.Service.BootstrapAsync(formId, recordId);
        var submission = await fixture.Service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(),
            FormVersionId = version.Id,
            RecordId = recordId,
            Values = new() { ["name"] = Json("After") }
        }, "worker");

        Assert.Equal("Before", bootstrap!.InitialValues["name"].GetString());
        Assert.True(submission.IsValid);
        fixture.DomainObjects.Verify(store => store.UpdateAsync("case", recordId,
            It.Is<IDictionary<string, object?>>(values => values.ContainsKey("name")), "worker"), Times.Once);
        fixture.DomainObjects.Verify(store => store.CreateAsync(
            It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Existing_record_bootstrap_formats_date_fields_for_the_form_protocol()
    {
        var formId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var version = Version(formId, "Task form", DateTime.UtcNow);
        version.Definition.Components =
        [
            new FormField { Name = "hearingDate", Label = "Hearing date", Type = "date" },
            new FormField { Name = "reviewDate", Label = "Review date", Type = "date" }
        ];
        await using var fixture = await Fixture.CreateAsync(version);
        fixture.DomainObjects.Setup(store => store.GetAsync("case", recordId))
            .ReturnsAsync(new DomainRecord
            {
                Id = recordId,
                ObjectKey = "case",
                Values = new()
                {
                    ["hearingDate"] = new DateTime(2026, 9, 5, 14, 30, 0, DateTimeKind.Utc),
                    ["reviewDate"] = Json("2026-10-12T00:00:00+02:00")
                }
            });
        fixture.FormData.Setup(store => store.GetCustomDataAsync(recordId, formId))
            .ReturnsAsync([]);

        var bootstrap = await fixture.Service.BootstrapAsync(formId, recordId);

        Assert.Equal("2026-09-05", bootstrap!.InitialValues["hearingDate"].GetString());
        Assert.Equal("2026-10-12", bootstrap.InitialValues["reviewDate"].GetString());
    }

    [Fact]
    public async Task Existing_record_bootstrap_formats_decimal_fields_as_protocol_strings()
    {
        var formId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var version = Version(formId, "Task form", DateTime.UtcNow);
        version.Definition.Components =
        [
            new FormField { Name = "total", Label = "Total", Type = "decimal" },
            new FormField { Name = "quantity", Label = "Quantity", Type = "integer" }
        ];
        await using var fixture = await Fixture.CreateAsync(version);
        fixture.DomainObjects.Setup(store => store.GetAsync("case", recordId))
            .ReturnsAsync(new DomainRecord
            {
                Id = recordId,
                ObjectKey = "case",
                Values = new()
                {
                    ["total"] = 123123m,
                    ["quantity"] = 12
                }
            });
        fixture.FormData.Setup(store => store.GetCustomDataAsync(recordId, formId))
            .ReturnsAsync([]);

        var bootstrap = await fixture.Service.BootstrapAsync(formId, recordId);

        Assert.Equal(JsonValueKind.String, bootstrap!.InitialValues["total"].ValueKind);
        Assert.Equal("123123", bootstrap.InitialValues["total"].GetString());
        Assert.Equal(JsonValueKind.Number, bootstrap.InitialValues["quantity"].ValueKind);
    }

    private static FormDesignVersion Version(Guid formId, string title, DateTime createdAt) => new()
    {
        FormDesignId = formId,
        CreatedAt = createdAt,
        Definition = new FormDefinition
        {
            Id = "publicForm",
            ObjectKey = "case",
            Title = title,
            Components = [new FormField { Name = "name", Label = "Name" }]
        }
    };

    private static FormField ReferenceField() => new()
    {
        Type = "choice",
        Name = "municipality",
        Label = "Municipality",
        Reference = new() { ObjectKey = "municipality", LabelField = "name" }
    };

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DbContextOptions<ArgentDbContext> options;
        public Mock<IDomainObjectDefinitionService> DomainDefinitions { get; } = new();
        public Mock<IDomainObjectStore> DomainObjects { get; } = new();
        public Mock<IFormDataStore> FormData { get; } = new();
        public FormRuntimeService Service { get; }

        private Fixture(DbContextOptions<ArgentDbContext> options)
        {
            this.options = options;
            Service = new FormRuntimeService(
                new TestDbContextFactory(options), DomainDefinitions.Object, DomainObjects.Object, FormData.Object);
        }

        public static async Task<Fixture> CreateAsync(params FormDesignVersion[] versions)
        {
            var options = new DbContextOptionsBuilder<ArgentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var db = new ArgentDbContext(options);
            db.FormDesignVersions.AddRange(versions);
            await db.SaveChangesAsync();
            return new Fixture(options);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ArgentDbContext> options)
        : IDbContextFactory<ArgentDbContext>
    {
        public ArgentDbContext CreateDbContext() => new(options);
    }
}
