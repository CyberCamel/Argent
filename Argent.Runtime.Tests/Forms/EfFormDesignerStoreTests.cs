using Argent.Core.Authorization;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Infrastructure.Data;
using Argent.Runtime.Forms.Stores;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Forms;

public sealed class EfFormDesignerStoreTests
{
    [Fact]
    public async Task Publish_allows_a_form_that_will_enrich_required_domain_properties_later()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using (var db = new ArgentDbContext(options))
        {
            var invoice = new DomainObject { Key = "invoice", Name = "Invoice" };
            db.DomainObjects.Add(invoice);
            db.DomainObjectVersions.Add(new DomainObjectVersion
            {
                DomainObjectId = invoice.Id, State = DomainObjectState.Published,
                Definition = new DomainObjectDefinition
                {
                    Key = "invoice", Properties = [new DomainProperty { Key = "number", Required = true }]
                }
            });
            await db.SaveChangesAsync();
        }
        var store = new EfFormDesignerStore(new TestDbContextFactory(options), Mock.Of<IResourceOwnershipService>());
        var saved = await store.SaveAsync(new FormDesignerSaveRequest
        {
            Name = "Invoice", Description = "", Definition = new FormDefinition
            {
                Id = "invoiceForm", ObjectKey = "invoice",
                Objects = [new() { Key = "invoice", ObjectKey = "invoice", IsPrimary = true }]
            }
        });

        var published = await store.PublishAsync(new FormPublishRequest
        {
            FormDesignId = saved.FormDesignId
        });
        await using var verify = new ArgentDbContext(options);
        Assert.Equal(published.Id, (await verify.FormDesignVersions.SingleAsync()).Id);
        Assert.Empty(await verify.FormDesignDrafts.ToListAsync());
    }

    [Fact]
    public async Task First_save_and_publish_preserves_authored_definition()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var store = new EfFormDesignerStore(
            new TestDbContextFactory(options), Mock.Of<IResourceOwnershipService>());
        var saved = await store.SaveAsync(new FormDesignerSaveRequest
        {
            Name = "Application",
            Description = "Public application form",
            UserName = "developer",
            Definition = new FormDefinition
            {
                Id = "citizenApplication",
                ObjectKey = "case",
                Title = "Citizen application",
                Components = [new FormField { Name = "applicantName", Label = "Applicant name" }]
            }
        });

        var published = await store.PublishAsync(new FormPublishRequest
        {
            FormDesignId = saved.FormDesignId,
            UserId = "developer"
        });
        var reloaded = await store.LoadAsync(saved.FormDesignId);

        Assert.Equal("Citizen application", published.Definition.Title);
        Assert.Equal("Citizen application", reloaded?.Definition?.Title);
        Assert.Equal("applicantName", Assert.IsType<FormField>(Assert.Single(reloaded!.Definition!.Components)).Name);
    }

    [Fact]
    public async Task Reload_without_draft_uses_latest_published_definition()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var formId = Guid.NewGuid();
        var latestVersionId = Guid.NewGuid();
        await using (var db = new ArgentDbContext(options))
        {
            db.FormDesigns.Add(new FormDesign { Id = formId, Name = "Application", ObjectKey = "case" });
            db.FormDesignVersions.AddRange(
                Published(formId, Guid.NewGuid(), "Old version", DateTime.UtcNow.AddMinutes(-1)),
                Published(formId, latestVersionId, "Citizen application", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var store = new EfFormDesignerStore(
            new TestDbContextFactory(options), Mock.Of<IResourceOwnershipService>());

        var reloaded = await store.LoadAsync(formId);

        Assert.NotNull(reloaded);
        Assert.Null(reloaded.DraftId);
        Assert.Equal(latestVersionId, reloaded.Versions.First().Id);
        Assert.Equal("Citizen application", reloaded.Definition?.Title);
        var field = Assert.IsType<FormField>(Assert.Single(reloaded.Definition!.Components));
        Assert.Equal("applicantName", field.Name);
    }

    private static FormDesignVersion Published(Guid formId, Guid versionId, string title, DateTime createdAt) => new()
    {
        Id = versionId,
        FormDesignId = formId,
        CreatedAt = createdAt,
        Definition = new FormDefinition
        {
            Id = "citizenApplication",
            ObjectKey = "case",
            Title = title,
            Components = [new FormField { Name = "applicantName", Label = "Applicant name" }]
        }
    };

    private sealed class TestDbContextFactory(DbContextOptions<ArgentDbContext> options)
        : IDbContextFactory<ArgentDbContext>
    {
        public ArgentDbContext CreateDbContext() => new(options);
    }
}
