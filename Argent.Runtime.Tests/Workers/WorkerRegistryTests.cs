using Argent.Core.Workers;
using Argent.Runtime.Workers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Argent.Runtime.Tests.Workers;

public class WorkerRegistryTests : WorkerQueueTestBase
{
    public WorkerRegistryTests() => BuildQueue();

    private static Worker NewWorker(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Subjects = "[]"
    };

    [Fact]
    public async Task Provisioning_starts_a_worker_offline_and_unreported()
    {
        // A provisioned worker has not connected yet, which is exactly the state an administrator
        // needs to be able to see: registered, but nothing known about the process behind it.
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));

        Assert.Equal(nameof(WorkerStatus.Offline), provisioned.Worker.Status.ToString());
        Assert.Null(provisioned.Worker.Runtime);
        Assert.Null(provisioned.Worker.LastHeartbeatAt);
        Assert.Empty(provisioned.Worker.GetSubjects());
        Assert.Equal(0, provisioned.Worker.KeyRotations);
    }

    [Fact]
    public async Task The_issued_key_is_the_key_that_was_stored()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));

        // The plaintext is never persisted, so the only way to confirm the pairing is to present
        // the key the caller was handed and see that it resolves.
        Assert.False(string.IsNullOrWhiteSpace(provisioned.ApiKey));
        Assert.DoesNotContain(provisioned.ApiKey, provisioned.Worker.ApiKeyHash);
        Assert.NotNull(await Registry.FindByApiKeyHashAsync(WorkerRegistry.HashApiKey(provisioned.ApiKey)));
    }

    [Fact]
    public async Task A_taken_name_is_refused_rather_than_taken_over()
    {
        await Registry.ProvisionAsync(NewWorker("reports"));

        // Provisioning is an administrator action, so a duplicate is reported instead of silently
        // replacing the existing worker and rotating its key out from under it.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Registry.ProvisionAsync(NewWorker("reports")));

        Assert.False(await Registry.NameIsAvailableAsync("reports"));
        Assert.True(await Registry.NameIsAvailableAsync("reports-2"));
    }

    [Fact]
    public async Task Rotating_a_key_invalidates_the_previous_one()
    {
        var first = await Registry.ProvisionAsync(NewWorker("reports"));

        var second = await Registry.RotateApiKeyAsync(first.Worker.Id);

        Assert.NotEqual(first.ApiKey, second.ApiKey);
        Assert.Equal(1, second.Worker.KeyRotations);

        // The old key stops working immediately, so a client holding it must be reconfigured.
        Assert.Null(await Registry.FindByApiKeyHashAsync(WorkerRegistry.HashApiKey(first.ApiKey)));
        Assert.NotNull(await Registry.FindByApiKeyHashAsync(WorkerRegistry.HashApiKey(second.ApiKey)));
    }

    [Fact]
    public async Task A_heartbeat_reports_the_runtime_and_subjects_the_client_claims()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));

        var accepted = await Registry.HeartbeatAsync(
            provisioned.Worker.Id,
            runtime: "Argent Python Worker 0.1",
            subjects: ["render", "export"],
            inFlight: 0,
            currentSubject: null);

        Assert.True(accepted);

        var reported = await Registry.GetByIdAsync(provisioned.Worker.Id);
        Assert.Equal("Argent Python Worker 0.1", reported!.Runtime);
        Assert.Equal(["render", "export"], reported.GetSubjects());
        Assert.Equal(nameof(WorkerStatus.Idle), reported.Status.ToString());
        Assert.NotNull(reported.LastHeartbeatAt);
    }

    [Fact]
    public async Task The_runtime_is_a_free_form_string_not_a_closed_set()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));

        // Nothing routes on the runtime, so a client is free to describe itself however it likes.
        await Registry.HeartbeatAsync(
            provisioned.Worker.Id,
            runtime: "Acme Report Renderer (internal build 4471, node 22)",
            subjects: null, inFlight: 0, currentSubject: null);

        Assert.Equal(
            "Acme Report Renderer (internal build 4471, node 22)",
            (await Registry.GetByIdAsync(provisioned.Worker.Id))!.Runtime);
    }

    [Fact]
    public async Task A_heartbeat_that_omits_a_field_leaves_the_stored_value_alone()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));

        await Registry.HeartbeatAsync(provisioned.Worker.Id, "Argent Python Worker 0.1", ["render"], 1, "render");
        await Registry.HeartbeatAsync(provisioned.Worker.Id, null, null, 0, null);

        // A minimal client must not erase detail an earlier, fuller report provided.
        var reported = await Registry.GetByIdAsync(provisioned.Worker.Id);
        Assert.Equal("Argent Python Worker 0.1", reported!.Runtime);
        Assert.Equal(["render"], reported.GetSubjects());
    }

    [Fact]
    public async Task Heartbeat_reports_idleness()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));

        Assert.True(await Registry.HeartbeatAsync(provisioned.Worker.Id, null, null, 0, null));
        Assert.Equal(WorkerStatus.Idle, (await Registry.GetByIdAsync(provisioned.Worker.Id))!.Status);

        Assert.True(await Registry.HeartbeatAsync(provisioned.Worker.Id, null, null, 2, "render"));
        var busy = await Registry.GetByIdAsync(provisioned.Worker.Id);
        Assert.Equal(WorkerStatus.Busy, busy!.Status);
        Assert.Equal(2, busy.InFlight);
        Assert.Equal("render", busy.CurrentSubject);
    }

    [Fact]
    public async Task A_heartbeat_does_not_revive_a_revoked_worker()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));
        await Registry.SetStatusAsync(provisioned.Worker.Id, WorkerStatus.Disabled);

        Assert.False(await Registry.HeartbeatAsync(provisioned.Worker.Id, "x", null, 0, null));
    }

    [Fact]
    public async Task A_revoked_worker_is_not_a_routing_target()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));
        Assert.True(await Registry.IsRegisteredAsync("reports"));

        await Registry.SetStatusAsync(provisioned.Worker.Id, WorkerStatus.Disabled);
        Assert.False(await Registry.IsRegisteredAsync("reports"));

        await Registry.SetStatusAsync(provisioned.Worker.Id, WorkerStatus.Offline);
        Assert.True(await Registry.IsRegisteredAsync("reports"));
    }

    [Fact]
    public async Task Deleting_removes_the_registration()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));

        Assert.True(await Registry.DeleteAsync(provisioned.Worker.Id));
        Assert.Null(await Registry.GetByIdAsync(provisioned.Worker.Id));
        Assert.Null(await Registry.FindByApiKeyHashAsync(WorkerRegistry.HashApiKey(provisioned.ApiKey)));

        // The name is free again, so a replacement can be provisioned.
        Assert.True(await Registry.NameIsAvailableAsync("reports"));
    }

    [Fact]
    public async Task Deleting_a_worker_keeps_the_requests_it_ran()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));
        var request = NewRequest(workerName: "reports");
        await Queue.EnqueueAsync(request);

        await Registry.DeleteAsync(provisioned.Worker.Id);

        // The request records what a running instance was told, so it must survive the process
        // that produced it and stay reachable from the admin and instance views.
        Assert.NotNull(await Queue.GetAsync(request.Id));
    }

    [Fact]
    public async Task A_worker_that_never_heartbeats_is_marked_offline()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));
        await Registry.HeartbeatAsync(provisioned.Worker.Id, null, null, 1, "render");

        var marked = await Registry.MarkStaleWorkersOfflineAsync(TimeSpan.Zero);

        Assert.Equal(1, marked);
        var stale = await Registry.GetByIdAsync(provisioned.Worker.Id);
        Assert.Equal(WorkerStatus.Offline, stale!.Status);
        Assert.Equal(0, stale.InFlight);

        // Still routable: a node waiting on a worker that may come back must not fail on the sweep.
        Assert.True(await Registry.IsRegisteredAsync("reports"));
    }

    [Fact]
    public async Task The_stale_sweep_does_not_touch_workers_that_are_already_settled()
    {
        var provisioned = await Registry.ProvisionAsync(NewWorker("reports"));
        await Registry.MarkStaleWorkersOfflineAsync(TimeSpan.Zero);
        await Registry.MarkStaleWorkersOfflineAsync(TimeSpan.Zero);

        Assert.Equal(WorkerStatus.Offline, (await Registry.GetByIdAsync(provisioned.Worker.Id))!.Status);
    }

    [Fact]
    public void Generated_api_keys_are_unique_and_hash_to_a_stable_value()
    {
        var (a, hashA) = WorkerRegistry.CreateApiKey();
        var (b, hashB) = WorkerRegistry.CreateApiKey();

        Assert.NotEqual(a, b);
        Assert.Equal(hashA, WorkerRegistry.HashApiKey(a));
        Assert.Equal(hashB, WorkerRegistry.HashApiKey(b));
        Assert.NotEqual(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }
}
