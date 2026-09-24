using System.Text.Json;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Runtime.Forms;
using Argent.Runtime.Workflows.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public sealed class MultiObjectFormTests
{
    [Fact]
    public async Task View_mode_requires_later_fields_and_allows_incomplete_record_at_start()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var formId = Guid.NewGuid();
        var version = new FormDesignVersion
        {
            FormDesignId = formId,
            Definition = new FormDefinition
            {
                Id = "stagedInvoice", ObjectKey = "invoice", ViewModes = ["approval"],
                Objects = [new() { Key = "invoice", ObjectKey = "invoice", IsPrimary = true }],
                Components =
                [
                    new FormField { Name = "invoice.number", ObjectBinding = "invoice", PropertyKey = "number",
                        Label = "Number", ModeOverrides = new() { ["approval"] = new() { Hidden = true } } },
                    new FormField { Name = "invoice.approvedBy", ObjectBinding = "invoice", PropertyKey = "approvedBy",
                        Label = "Approved by", ModeOverrides = new() { ["approval"] = new() { Required = true } } }
                ]
            }
        };
        await using (var db = new ArgentDbContext(options))
        {
            var invoice = new DomainObject { Key = "invoice", Name = "Invoice" };
            db.DomainObjects.Add(invoice);
            db.DomainObjectVersions.Add(new DomainObjectVersion { DomainObjectId = invoice.Id,
                State = DomainObjectState.Published,
                Definition = new DomainObjectDefinition { Key = "invoice", Properties =
                    [new() { Key = "number", Type = DomainPropertyType.Text, Required = true },
                     new() { Key = "approvedBy", Type = DomainPropertyType.Text, Required = true }] } });
            db.FormDesignVersions.Add(version);
            await db.SaveChangesAsync();
        }

        var domainObjects = new Mock<IDomainObjectStore>();
        var formData = new Mock<IFormDataStore>();
        var service = new FormRuntimeService(new Factory(options), Mock.Of<IDomainObjectDefinitionService>(),
            domainObjects.Object, formData.Object);
        var created = await service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = version.Id,
            Values = new() { ["invoice.number"] = JsonSerializer.SerializeToElement("INV-1") }
        }, "starter");
        Assert.True(created.IsValid, string.Join("; ", created.Errors.Select(error => error.Message)));

        domainObjects.Setup(store => store.GetAsync("invoice", created.Result!.RecordId))
            .ReturnsAsync(new DomainRecord { Id = created.Result!.RecordId, ObjectKey = "invoice",
                Values = new() { ["number"] = "INV-1" } });
        formData.Setup(store => store.GetCustomDataAsync(created.Result!.RecordId, formId))
            .ReturnsAsync([]);
        var bootstrap = await service.BootstrapAsync(formId, created.Result!.RecordIds,
            viewMode: "approval");
        Assert.DoesNotContain("invoice.number", bootstrap!.InitialValues.Keys);

        var approval = FormViewModeProjector.Apply(version.Definition, "approval");
        Assert.True(((FormField)approval.Components[0]).Hidden);
        Assert.Contains(FormValueValidator.Validate(approval, new Dictionary<string, JsonElement>()),
            error => error.Field == "invoice.approvedBy" && error.Code == "field.required");
        var completed = await service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = version.Id,
            RecordIds = created.Result!.RecordIds,
            Values = new()
            {
                ["invoice.number"] = JsonSerializer.SerializeToElement("TAMPERED"),
                ["invoice.approvedBy"] = JsonSerializer.SerializeToElement("alexb")
            }
        }, "alexb", updateAttachedRecords: true, viewMode: "approval");
        Assert.True(completed.IsValid, string.Join("; ", completed.Errors.Select(error => error.Message)));
        await using var check = new ArgentDbContext(options);
        var record = await check.DomainObjectRecords.FindAsync(created.Result.RecordId);
        Assert.Equal("INV-1", record!.Values["number"]?.ToString());
        Assert.Equal("alexb", record.Values["approvedBy"]?.ToString());
    }

    [Fact]
    public async Task Same_form_updates_attached_record_and_creates_secondary_object_when_later_populated()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var formId = Guid.NewGuid();
        var version = new FormDesignVersion
        {
            FormDesignId = formId,
            Definition = new FormDefinition
            {
                Id = "progressiveInvoice", ObjectKey = "invoice",
                Objects =
                [
                    new() { Key = "invoice", ObjectKey = "invoice", IsPrimary = true },
                    new() { Key = "customer", ObjectKey = "customer" }
                ],
                Components =
                [
                    new FormField { Name = "invoice.number", ObjectBinding = "invoice", PropertyKey = "number", Label = "Number", Required = true },
                    new FormField { Name = "customer.name", ObjectBinding = "customer", PropertyKey = "name", Label = "Customer", Required = true },
                    new FormField { Name = "customer.email", ObjectBinding = "customer", PropertyKey = "email", Label = "Email" }
                ]
            }
        };
        await using (var db = new ArgentDbContext(options))
        {
            var invoice = new DomainObject { Key = "invoice", Name = "Invoice" };
            var customer = new DomainObject { Key = "customer", Name = "Customer" };
            db.DomainObjects.AddRange(invoice, customer);
            db.DomainObjectVersions.AddRange(
                new DomainObjectVersion { DomainObjectId = invoice.Id, State = DomainObjectState.Published,
                    Definition = new DomainObjectDefinition { Key = "invoice", Properties = [new() { Key = "number", Type = DomainPropertyType.Text, Required = true }] } },
                new DomainObjectVersion { DomainObjectId = customer.Id, State = DomainObjectState.Published,
                    Definition = new DomainObjectDefinition { Key = "customer", Properties =
                        [new() { Key = "name", Type = DomainPropertyType.Text, Required = true }, new() { Key = "email", Type = DomainPropertyType.Text }] } });
            db.FormDesignVersions.Add(version);
            await db.SaveChangesAsync();
        }

        var service = new FormRuntimeService(new Factory(options), Mock.Of<IDomainObjectDefinitionService>(),
            Mock.Of<IDomainObjectStore>(), Mock.Of<IFormDataStore>());
        var first = await service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = version.Id,
            Values = new() { ["invoice.number"] = JsonSerializer.SerializeToElement("INV-1") }
        }, "starter");
        Assert.True(first.IsValid, string.Join("; ", first.Errors.Select(error => error.Message)));
        Assert.Single(first.Result!.RecordIds);

        var incomplete = new Dictionary<string, JsonElement>
        {
            ["invoice.number"] = JsonSerializer.SerializeToElement("INV-1"),
            ["customer.email"] = JsonSerializer.SerializeToElement("contact@example.com")
        };
        Assert.Contains(FormValueValidator.Validate(version.Definition, incomplete),
            error => error.Field == "customer.name" && error.Code == "field.required");

        var wrongContext = await service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = version.Id,
            RecordIds = first.Result.RecordIds,
            Values = new() { ["invoice.number"] = JsonSerializer.SerializeToElement("INV-2") }
        }, "starter");
        Assert.Contains(wrongContext.Errors, error => error.Code == "object.already_created");

        var second = await service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = version.Id,
            RecordIds = first.Result.RecordIds,
            Values = new()
            {
                ["invoice.number"] = JsonSerializer.SerializeToElement("INV-2"),
                ["customer.name"] = JsonSerializer.SerializeToElement("Acme")
            }
        }, "reviewer", updateAttachedRecords: true);
        Assert.True(second.IsValid, string.Join("; ", second.Errors.Select(error => error.Message)));
        Assert.Equal(2, second.Result!.RecordIds.Count);
        Assert.Equal(first.Result.RecordId, second.Result.RecordId);

        var third = await service.SubmitAsync(formId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = version.Id,
            RecordIds = second.Result.RecordIds,
            Values = new()
            {
                ["invoice.number"] = JsonSerializer.SerializeToElement("INV-2"),
                ["customer.name"] = JsonSerializer.SerializeToElement("Acme Updated")
            }
        }, "reviewer", updateAttachedRecords: true);
        Assert.True(third.IsValid, string.Join("; ", third.Errors.Select(error => error.Message)));
        Assert.Equal(second.Result.RecordIds["customer"], third.Result!.RecordIds["customer"]);
        await using var check = new ArgentDbContext(options);
        Assert.Equal(2, await check.DomainObjectRecords.CountAsync());
        Assert.Equal("INV-2", (await check.DomainObjectRecords.FindAsync(first.Result.RecordId))!.Values["number"]?.ToString());
        Assert.Equal("Acme Updated", (await check.DomainObjectRecords.FindAsync(third.Result.RecordIds["customer"]))!.Values["name"]?.ToString());
    }

    [Fact]
    public async Task Workflow_instance_keeps_record_bindings_for_later_tasks()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ArgentDbContext(options);
        var primaryId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var instance = new WorkflowInstance
        {
            RecordId = primaryId,
            RecordBindingsJson = JsonSerializer.Serialize(new Dictionary<string, Guid>
            {
                ["invoice"] = primaryId, ["customer"] = customerId
            })
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        var service = new WorkflowInstanceService(db, Mock.Of<IAuditService>(),
            NullLogger<WorkflowInstanceService>.Instance);

        var snapshot = await service.GetStateAsync(instance.InstanceId, CancellationToken.None);
        Assert.Equal(customerId, snapshot.RecordIds["customer"]);
        var updated = new Dictionary<string, Guid>(snapshot.RecordIds) { ["approver"] = Guid.NewGuid() };
        await service.SaveRecordIdsAsync(instance.InstanceId, updated, CancellationToken.None);
        Assert.Equal(updated["approver"],
            (await service.GetStateAsync(instance.InstanceId, CancellationToken.None)).RecordIds["approver"]);
    }

    [Fact]
    public void Inactive_object_does_not_require_its_fields()
    {
        var definition = new FormDefinition
        {
            Id = "optionalCustomer", ObjectKey = "invoice",
            Objects =
            [
                new() { Key = "invoice", ObjectKey = "invoice", IsPrimary = true },
                new()
                {
                    Key = "customer", ObjectKey = "customer",
                    AssignToBinding = "invoice", AssignToProperty = "customer",
                    When = new FormExpression
                    {
                        Operator = "equals",
                        Left = new FormOperand { Field = "createCustomer" },
                        Right = new FormOperand { Value = JsonSerializer.SerializeToElement(true) }
                    }
                }
            ],
            Components =
            [
                new FormField { Name = "createCustomer", Type = "boolean", Label = "Create customer" },
                new FormField { Name = "invoice.customer", ObjectBinding = "invoice", PropertyKey = "customer", Type = "choice", Label = "Existing customer", Required = true,
                    Reference = new FormReferenceSource { ObjectKey = "customer", LabelField = "name" } },
                new FormField { Name = "customer.name", ObjectBinding = "customer", PropertyKey = "name", Label = "Name", Required = true }
            ]
        };
        Assert.True(FormDefinitionCompiler.Compile(definition).IsValid);
        Assert.Empty(FormValueValidator.Validate(definition, new Dictionary<string, JsonElement>
        {
            ["createCustomer"] = JsonSerializer.SerializeToElement(false),
            ["invoice.customer"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString())
        }));
        Assert.Contains(FormValueValidator.Validate(definition, new Dictionary<string, JsonElement>
        {
            ["createCustomer"] = JsonSerializer.SerializeToElement(true)
        }), error => error.Field == "customer.name" && error.Code == "field.required");
    }

    [Fact]
    public async Task Submission_creates_linked_records_and_later_task_updates_primary_record()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var invoiceFormId = Guid.NewGuid();
        var taskFormId = Guid.NewGuid();
        var create = new FormDesignVersion
        {
            FormDesignId = invoiceFormId,
            Definition = new FormDefinition
            {
                Id = "invoiceCreate", ObjectKey = "invoice",
                Objects =
                [
                    new() { Key = "invoice", ObjectKey = "invoice", IsPrimary = true },
                    new() { Key = "customer", ObjectKey = "customer", AssignToBinding = "invoice", AssignToProperty = "customer" }
                ],
                Components =
                [
                    new FormField { Name = "invoice.amount", ObjectBinding = "invoice", PropertyKey = "amount", Type = "decimal", Label = "Amount", Required = true },
                    new FormField { Name = "customer.name", ObjectBinding = "customer", PropertyKey = "name", Label = "Customer", Required = true }
                ]
            }
        };
        var update = new FormDesignVersion
        {
            FormDesignId = taskFormId,
            Definition = new FormDefinition
            {
                Id = "invoiceTask", ObjectKey = "invoice",
                Objects = [new() { Key = "invoice", ObjectKey = "invoice", IsPrimary = true }],
                Components = [new FormField { Name = "invoice.amount", ObjectBinding = "invoice", PropertyKey = "amount", Type = "decimal", Label = "Amount", Required = true }]
            }
        };
        await using (var db = new ArgentDbContext(options))
        {
            var invoice = new DomainObject { Key = "invoice", Name = "Invoice" };
            var customer = new DomainObject { Key = "customer", Name = "Customer" };
            db.DomainObjects.AddRange(invoice, customer);
            db.DomainObjectVersions.AddRange(
                new DomainObjectVersion
                {
                    DomainObjectId = invoice.Id, State = DomainObjectState.Published,
                    Definition = new DomainObjectDefinition
                    {
                        Key = "invoice", Properties =
                        [
                            new() { Key = "amount", Type = DomainPropertyType.Number, Required = true },
                            new() { Key = "customer", Type = DomainPropertyType.Reference, Required = true, ReferenceTargetKey = "customer" }
                        ]
                    }
                },
                new DomainObjectVersion
                {
                    DomainObjectId = customer.Id, State = DomainObjectState.Published,
                    Definition = new DomainObjectDefinition
                    {
                        Key = "customer", Properties = [new() { Key = "name", Type = DomainPropertyType.Text, Required = true }]
                    }
                });
            db.FormDesignVersions.AddRange(create, update);
            await db.SaveChangesAsync();
        }

        var domainObjects = new Mock<IDomainObjectStore>();
        var formData = new Mock<IFormDataStore>();
        var service = new FormRuntimeService(new Factory(options),
            Mock.Of<IDomainObjectDefinitionService>(), domainObjects.Object, formData.Object);
        var submitted = await service.SubmitAsync(invoiceFormId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = create.Id,
            Values = new()
            {
                ["invoice.amount"] = JsonSerializer.SerializeToElement("25.00"),
                ["customer.name"] = JsonSerializer.SerializeToElement("Acme")
            }
        }, "tester");

        Assert.True(submitted.IsValid, string.Join("; ", submitted.Errors.Select(error => error.Message)));
        Assert.Equal(2, submitted.Result!.RecordIds.Count);
        await using (var db = new ArgentDbContext(options))
        {
            Assert.Equal(2, await db.DomainObjectRecords.CountAsync());
            var invoice = await db.DomainObjectRecords.FindAsync(submitted.Result.RecordId);
            Assert.Equal(submitted.Result.RecordIds["customer"].ToString(), invoice!.Values["customer"]?.ToString());
        }

        domainObjects.Setup(store => store.GetAsync("invoice", submitted.Result.RecordId))
            .ReturnsAsync(new DomainRecord
            {
                Id = submitted.Result.RecordId, ObjectKey = "invoice",
                Values = new() { ["amount"] = 25.00m, ["customer"] = submitted.Result.RecordIds["customer"] }
            });
        formData.Setup(store => store.GetCustomDataAsync(submitted.Result.RecordId, taskFormId))
            .ReturnsAsync([]);
        var bootstrap = await service.BootstrapAsync(taskFormId, submitted.Result.RecordIds);
        Assert.Equal("25.00", bootstrap!.InitialValues["invoice.amount"].GetString());

        var changed = await service.SubmitAsync(taskFormId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = update.Id,
            RecordIds = submitted.Result.RecordIds,
            Values = new() { ["invoice.amount"] = JsonSerializer.SerializeToElement("30.00") }
        }, "reviewer", updateAttachedRecords: true);

        Assert.True(changed.IsValid, string.Join("; ", changed.Errors.Select(error => error.Message)));
        await using (var db = new ArgentDbContext(options))
        {
            Assert.Equal(2, await db.DomainObjectRecords.CountAsync());
            var invoice = await db.DomainObjectRecords.FindAsync(submitted.Result.RecordId);
            Assert.Equal("30", invoice!.Values["amount"]?.ToString());
            Assert.Equal(submitted.Result.RecordIds["customer"].ToString(), invoice.Values["customer"]?.ToString());
        }

        var selectFormId = Guid.NewGuid();
        var selectVersion = new FormDesignVersion
        {
            FormDesignId = selectFormId,
            Definition = new FormDefinition
            {
                Id = "invoiceExistingCustomer", ObjectKey = "invoice",
                Objects =
                [
                    new() { Key = "invoice", ObjectKey = "invoice", IsPrimary = true },
                    new()
                    {
                        Key = "newCustomer", ObjectKey = "customer",
                        AssignToBinding = "invoice", AssignToProperty = "customer",
                        When = new FormExpression
                        {
                            Operator = "equals",
                            Left = new FormOperand { Field = "createNew" },
                            Right = new FormOperand { Value = JsonSerializer.SerializeToElement(true) }
                        }
                    }
                ],
                Components =
                [
                    new FormField { Name = "createNew", Type = "boolean", Label = "Create a new customer" },
                    new FormField { Name = "invoice.amount", ObjectBinding = "invoice", PropertyKey = "amount", Type = "decimal", Label = "Amount", Required = true },
                    new FormField { Name = "invoice.customer", ObjectBinding = "invoice", PropertyKey = "customer", Type = "choice", Label = "Existing customer", Required = true,
                        Reference = new FormReferenceSource { ObjectKey = "customer", LabelField = "name" } },
                    new FormField { Name = "newCustomer.name", ObjectBinding = "newCustomer", PropertyKey = "name", Label = "New customer name", Required = true }
                ]
            }
        };
        await using (var db = new ArgentDbContext(options))
        {
            db.FormDesignVersions.Add(selectVersion);
            await db.SaveChangesAsync();
        }
        domainObjects.Setup(store => store.GetAsync("customer", submitted.Result.RecordIds["customer"]))
            .ReturnsAsync(new DomainRecord { Id = submitted.Result.RecordIds["customer"], ObjectKey = "customer" });
        var selected = await service.SubmitAsync(selectFormId, new FormSubmitRequest
        {
            SubmissionId = Guid.NewGuid(), FormVersionId = selectVersion.Id,
            Values = new()
            {
                ["createNew"] = JsonSerializer.SerializeToElement(false),
                ["invoice.amount"] = JsonSerializer.SerializeToElement("40.00"),
                ["invoice.customer"] = JsonSerializer.SerializeToElement(submitted.Result.RecordIds["customer"].ToString())
            }
        }, "tester");
        Assert.True(selected.IsValid, string.Join("; ", selected.Errors.Select(error => error.Message)));
        Assert.Single(selected.Result!.RecordIds);
        await using (var db = new ArgentDbContext(options))
            Assert.Equal(3, await db.DomainObjectRecords.CountAsync());
    }

    private sealed class Factory(DbContextOptions<ArgentDbContext> options) : IDbContextFactory<ArgentDbContext>
    {
        public ArgentDbContext CreateDbContext() => new(options);
    }
}
