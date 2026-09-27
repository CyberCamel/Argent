using Argent.Core.Workers;
using Argent.Runtime.Workers;
using Microsoft.AspNetCore.Mvc;

namespace Argent.Web.Api;

/// <summary>
/// The worker-facing API. External worker processes authenticate with a bearer API key rather than
/// an interactive session, so these endpoints are anonymous to the identity system and authorised
/// by the key alone.
///
/// There is no register endpoint. An administrator provisions a worker from
/// <c>/Admin/Workers</c>, which issues the key and shows it once; from then on the client only
/// presents it and reports what it is.
/// </summary>
public static class WorkerApi
{
    /// <summary>Default lease granted on claim. Long tasks renew it; the author's timeout is the hard budget.</summary>
    public static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(60);

    public static IEndpointRouteBuilder MapWorkerApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workers").AllowAnonymous();

        // ── Identity ─────────────────────────────────────────────────────────
        // Lets a client confirm its key is still valid and learn what the server expects of it,
        // rather than discovering a problem from a failed claim.

        group.MapGet("/me", async (
            IWorkerRegistry registry,
            HttpContext context,
            CancellationToken ct) =>
        {
            var worker = await AuthenticateAsync(registry, context, ct);
            if (worker == null) return Unauthorized();

            return Results.Ok(new WorkerIdentityResponse(
                worker.Id,
                worker.Name,
                worker.Status.ToString(),
                worker.Concurrency,
                worker.Runtime,
                worker.GetSubjects()));
        });

        // ── Heartbeat ────────────────────────────────────────────────────────
        // Also the only place a client describes itself: runtime and subjects are reported here,
        // because without a registration call there is nowhere else to say them.

        group.MapPost("/heartbeat", async (
            [FromBody] WorkerHeartbeatRequest? request,
            IWorkerRegistry registry,
            HttpContext context,
            CancellationToken ct) =>
        {
            var worker = await AuthenticateAsync(registry, context, ct);
            if (worker == null) return Unauthorized();

            request ??= new WorkerHeartbeatRequest();

            var updated = await registry.HeartbeatAsync(
                worker.Id,
                request.Runtime,
                request.Subjects,
                request.InFlight,
                request.CurrentSubject,
                ct);

            if (!updated)
                return Results.Json(new { error = "This worker has been revoked or deleted." }, statusCode: 410);

            var current = await registry.GetByIdAsync(worker.Id, ct);
            return Results.Ok(new WorkerHeartbeatResponse(
                current?.Status.ToString() ?? nameof(WorkerStatus.Offline),
                worker.Name,
                current?.Concurrency ?? worker.Concurrency,
                DateTime.UtcNow));
        });

        // ── Claim ─────────────────────────────────────────────────────────────

        group.MapPost("/requests/claim", async (
            [FromBody] WorkerClaimRequest? request,
            IWorkerRequestQueue queue,
            IWorkerRegistry registry,
            HttpContext context,
            CancellationToken ct) =>
        {
            var worker = await AuthenticateAsync(registry, context, ct);
            if (worker == null) return Unauthorized();

            // The authenticated worker is the only identity that matters, so a body-supplied value
            // cannot be used to claim as somebody else. The worker also caps itself.
            var requested = request?.MaxItems ?? 1;
            var maxItems = Math.Clamp(requested <= 0 ? 1 : requested, 1, Math.Max(1, worker.Concurrency));

            var result = await queue.ClaimAsync(worker.Id, worker.Name, maxItems, DefaultLease, ct);

            var tasks = result.Requests
                .Select(r => new WorkerTaskPayload(
                    r.Id,
                    r.InstanceId,
                    r.TokenId,
                    r.NodeId,
                    r.WorkerName,
                    r.Subject,
                    r.GetParameters(),
                    r.Attempt,
                    r.MaxAttempts,
                    r.TimeoutSeconds,
                    r.LeaseExpiresAt ?? DateTime.UtcNow.Add(DefaultLease)))
                .ToList();

            return Results.Ok(tasks);
        });

        // ── Lease renewal ─────────────────────────────────────────────────────

        group.MapPost("/requests/{id:guid}/renew", async (
            Guid id,
            [FromBody] WorkerRenewLeaseRequest? renew,
            IWorkerRequestQueue queue,
            IWorkerRegistry registry,
            HttpContext context,
            CancellationToken ct) =>
        {
            var worker = await AuthenticateAsync(registry, context, ct);
            if (worker == null) return Unauthorized();

            var lease = renew?.LeaseSeconds is > 0 ? TimeSpan.FromSeconds(renew.LeaseSeconds.Value) : DefaultLease;

            return await queue.RenewLeaseAsync(id, worker.Id, lease, ct)
                ? Results.Ok(new { id, leaseExpiresAt = DateTime.UtcNow.Add(lease) })
                : Results.Json(
                    new { error = "The request is not held by this worker, or it has already finished." },
                    statusCode: 409);
        });

        // ── Completion ────────────────────────────────────────────────────────

        group.MapPost("/requests/{id:guid}/complete", async (
            Guid id,
            [FromBody] WorkerCompleteRequest request,
            IWorkerRequestQueue queue,
            IWorkerRegistry registry,
            HttpContext context,
            CancellationToken ct) =>
        {
            var worker = await AuthenticateAsync(registry, context, ct);
            if (worker == null) return Unauthorized();

            if (!request.Succeeded && string.IsNullOrWhiteSpace(request.Error))
                return Results.BadRequest(new { error = "A failed task must include an error message." });

            var completed = await queue.CompleteAsync(
                id, worker.Id, request.Succeeded, request.Outputs, request.Error, ct);

            if (completed == null)
                return Results.NotFound(new { error = $"No worker request with id {id}." });

            if (completed.State is not (WorkerRequestState.Succeeded or WorkerRequestState.Failed))
            {
                // Reclaimed or already finished. Report the real state rather than pretending the
                // result was recorded, so the client can decide whether to read it back.
                return Results.Json(ToStatus(completed), statusCode: 409);
            }

            return Results.Ok(ToStatus(completed));
        });

        // ── Status read-back ──────────────────────────────────────────────────
        // A worker whose completion response was lost can ask whether its result landed.

        group.MapGet("/requests/{id:guid}", async (
            Guid id,
            IWorkerRequestQueue queue,
            IWorkerRegistry registry,
            HttpContext context,
            CancellationToken ct) =>
        {
            var worker = await AuthenticateAsync(registry, context, ct);
            if (worker == null) return Unauthorized();

            var request = await queue.GetAsync(id, ct);
            if (request == null)
                return Results.NotFound(new { error = $"No worker request with id {id}." });

            return Results.Ok(ToStatus(request));
        });

        return app;
    }

    private static WorkerRequestStatusResponse ToStatus(WorkerRequest request) => new(
        request.Id,
        request.State.ToString(),
        request.Attempt,
        request.LeaseExpiresAt,
        request.GetOutputs(),
        request.ErrorMessage);

    /// <summary>
    /// Resolves the worker behind the bearer key. The presented secret is hashed and matched
    /// against the stored hash, so a wrong key reveals nothing beyond a 401 and the plaintext key
    /// is never stored or logged. Returns null for a missing, unknown, or revoked worker.
    /// </summary>
    private static async Task<Worker?> AuthenticateAsync(
        IWorkerRegistry registry,
        HttpContext context,
        CancellationToken ct)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var presented = header["Bearer ".Length..].Trim();
        if (presented.Length == 0) return null;

        var worker = await registry.FindByApiKeyHashAsync(WorkerRegistry.HashApiKey(presented), ct);
        return worker is { Status: not WorkerStatus.Disabled } ? worker : null;
    }

    private static IResult Unauthorized() => Results.Json(
        new { error = "A valid worker API key is required. Ask an administrator to provision this worker." },
        statusCode: 401);
}
