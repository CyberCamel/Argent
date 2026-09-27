using Argent.Core.Workers;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Argent.Web.Pages.Admin.Workers;

[Authorize(Policy = "FlowAdminOnly")]
public class IndexModel(ArgentDbContext _ctx, IWorkerRegistry _registry) : PageModel
{
    public List<WorkerListItem> Workers { get; set; } = [];

    [BindProperty]
    public string NewWorkerName { get; set; } = string.Empty;

    [BindProperty]
    public string? NewWorkerDisplayName { get; set; }

    [BindProperty]
    public int NewWorkerConcurrency { get; set; } = 1;

    /// <summary>
    /// The plaintext API key for a worker just provisioned or rotated. Set on the response that
    /// created it and never rendered again; only its hash is persisted.
    /// </summary>
    public string? IssuedApiKey { get; private set; }

    public string? IssuedApiKeyWorkerName { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostProvisionAsync()
    {
        var name = NewWorkerName?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Error = "A worker name is required.";
            await LoadAsync();
            return Page();
        }

        if (name.Length > 128)
        {
            Error = "Worker names are limited to 128 characters.";
            await LoadAsync();
            return Page();
        }

        if (!await _registry.NameIsAvailableAsync(name))
        {
            Error = $"A worker named '{name}' is already registered.";
            await LoadAsync();
            return Page();
        }

        var worker = new Worker
        {
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(NewWorkerDisplayName) ? name : NewWorkerDisplayName!.Trim(),
            Concurrency = Math.Clamp(NewWorkerConcurrency <= 0 ? 1 : NewWorkerConcurrency, 1, 64)
        };

        try
        {
            var provisioned = await _registry.ProvisionAsync(worker);
            IssuedApiKey = provisioned.ApiKey;
            IssuedApiKeyWorkerName = provisioned.Worker.Name;
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
            await LoadAsync();
            return Page();
        }
        NewWorkerName = string.Empty;
        NewWorkerDisplayName = null;
        NewWorkerConcurrency = 1;

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRotateKeyAsync(Guid id)
    {
        try
        {
            // The registry returns the plaintext it hashed, so the key shown here is the key that
            // is now stored. A revoked worker stays revoked; only its key changed.
            var rotated = await _registry.RotateApiKeyAsync(id);
            IssuedApiKey = rotated.ApiKey;
            IssuedApiKeyWorkerName = rotated.Worker.Name;
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        await LoadAsync();
        return Page();
    }

    /// <summary>
    /// Revokes or restores a worker. Revoking leaves the registration and its history in place and
    /// simply stops the key working, which is the reversible option; deleting is the permanent one.
    /// </summary>
    public async Task<IActionResult> OnPostToggleRevokedAsync(Guid id)
    {
        var worker = await _ctx.Workers.FindAsync(id);
        if (worker == null) return NotFound();

        await _registry.SetStatusAsync(
            id,
            worker.Status == WorkerStatus.Disabled ? WorkerStatus.Offline : WorkerStatus.Disabled);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        // Request history is deliberately retained, so deleting only removes the registration.
        if (!await _registry.DeleteAsync(id)) return NotFound();

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var workers = await _ctx.Workers.AsNoTracking().OrderBy(w => w.Name).ToListAsync();

        // Outstanding work is what an administrator actually needs to see: a revoked worker holding
        // nothing is noise, and one holding claimed requests is a stall.
        var outstanding = await _ctx.WorkerRequests
            .GroupBy(r => r.WorkerName)
            .Select(g => new
            {
                WorkerName = g.Key,
                Claimed = g.Count(r => r.State == WorkerRequestState.Claimed),
                Pending = g.Count(r => r.State == WorkerRequestState.Pending)
            })
            .ToDictionaryAsync(g => g.WorkerName);

        Workers = workers.Select(w =>
        {
            outstanding.TryGetValue(w.Name, out var counts);
            return new WorkerListItem
            {
                Id = w.Id,
                Name = w.Name,
                DisplayName = w.DisplayName,
                Runtime = w.Runtime,
                Status = w.Status,
                Subjects = w.GetSubjects(),
                Concurrency = w.Concurrency,
                InFlight = w.InFlight,
                CurrentSubject = w.CurrentSubject,
                LastHeartbeatAt = w.LastHeartbeatAt,
                RegisteredAt = w.RegisteredAt,
                KeyRotations = w.KeyRotations,
                ClaimedRequests = counts?.Claimed ?? 0,
                PendingRequests = counts?.Pending ?? 0
            };
        }).ToList();
    }

    public record WorkerListItem
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }
        public string? DisplayName { get; init; }

        /// <summary>What the client reported about itself, or null if it has never connected.</summary>
        public string? Runtime { get; init; }

        public required WorkerStatus Status { get; init; }
        public required IReadOnlyList<string> Subjects { get; init; }
        public required int Concurrency { get; init; }
        public required int InFlight { get; init; }
        public string? CurrentSubject { get; init; }
        public DateTime? LastHeartbeatAt { get; init; }
        public required DateTime RegisteredAt { get; init; }
        public required int KeyRotations { get; init; }
        public required int ClaimedRequests { get; init; }
        public required int PendingRequests { get; init; }
    }
}
