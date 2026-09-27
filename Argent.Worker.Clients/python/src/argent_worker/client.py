"""The worker client: identify itself, claim, dispatch, complete."""

from __future__ import annotations

import logging
import platform
import signal
import threading
import time
from concurrent.futures import Future, ThreadPoolExecutor, wait
from types import FrameType
from typing import Any, Callable

from . import __version__ as __package_version__
from .errors import TaskFailure, WorkerConfigError, WorkerProtocolError
from .task import Handler, Task
from .transport import HttpTransport, Transport

logger = logging.getLogger("argent_worker")

#: Reported to the server so an administrator can tell one worker build from another. Free-form
#: by design: nothing routes on it, and a client may describe itself however it likes.
DEFAULT_RUNTIME = f"Argent Python Worker {__package_version__}"

# How long a task may run after the client stops accepting work, so a Ctrl-C does not abandon a
# claimed request to a lease expiry.
DEFAULT_SHUTDOWN_GRACE_SECONDS = 30.0


class WorkerClient:
    """A worker process.

    Args:
        name: The routing key a `WorkerActivity` node targets. Must match exactly.
        base_url: Root of the Argent web app, for example `https://localhost:5001`.
        subjects: Task identifiers this worker has handlers for. Advertised to the engine and
            shown in the admin worker list; it is not enforced, so a mismatch surfaces as a
            request that is never claimed.
        api_key: Existing key. When omitted the client registers and receives one; the key is
            held in memory only, so pass it explicitly for a long-lived worker rather than
            re-registering on every start.
        concurrency: How many tasks to run at once. Also the claim batch size.
        poll_interval: Seconds between claim attempts when there is spare capacity.
        heartbeat_interval: Seconds between heartbeats. A worker is marked offline after roughly
            60s without one, so keep this comfortably below that.
        transport: Injected in tests; defaults to HTTP against `base_url`.
    """

    def __init__(
        self,
        api_key: str,
        base_url: str,
        *,
        name: str | None = None,
        subjects: list[str] | None = None,
        runtime: str = DEFAULT_RUNTIME,
        concurrency: int = 1,
        poll_interval: float = 1.0,
        heartbeat_interval: float = 20.0,
        transport: Transport | None = None,
        shutdown_grace: float = DEFAULT_SHUTDOWN_GRACE_SECONDS,
    ) -> None:
        if not api_key or not api_key.strip():
            raise WorkerConfigError(
                "an API key is required. Provision the worker under Admin → Workers and copy "
                "the key it shows."
            )
        if concurrency < 1:
            raise WorkerConfigError("concurrency must be at least 1")

        self.api_key = api_key.strip()
        self.base_url = base_url
        self.expected_name = name.strip() if name else None
        self.runtime = runtime
        self.concurrency = concurrency
        self.poll_interval = poll_interval
        self.heartbeat_interval = heartbeat_interval
        self.shutdown_grace = shutdown_grace

        self._handlers: dict[str, Handler] = {}
        self._declared_subjects: frozenset[str] = frozenset(
            s.strip() for s in (subjects or []) if s and s.strip()
        )
        self._transport: Transport = transport or HttpTransport(base_url)
        # Whatever the server says this key resolves to. Set by verify().
        self.name: str | None = self.expected_name
        self._stopping = threading.Event()
        self._in_flight = 0
        self._in_flight_lock = threading.Lock()

    # ── Authoring ────────────────────────────────────────────────────────

    @property
    def subjects(self) -> list[str]:
        """The subjects this worker handles, whether declared up front or discovered by `@task`."""
        return sorted(set(self._handlers) | set(self._declared_subjects))

    def task(self, subject: str) -> Callable[[Handler], Handler]:
        """Register a handler for a subject.

            @client.task("render")
            def render(task: Task) -> dict[str, str]:
                return {"reportUrl": render_pdf(task.require("customer"))}

        The handler's return value is sent back as the task's outputs. Returning `None` succeeds
        with no outputs; raising `TaskFailure` fails the workflow node with that message.
        """

        def decorate(handler: Handler) -> Handler:
            if not subject or not subject.strip():
                raise WorkerConfigError("a task subject is required")
            self._handlers[subject.strip()] = handler
            return handler

        return decorate

    # ── Lifecycle ────────────────────────────────────────────────────────

    def verify(self) -> str:
        """Confirm the key is valid and learn which worker it belongs to. Returns the name.

        Worth calling at startup: a revoked or rotated key fails here with a clear message, rather
        than as a claim that silently never returns anything.
        """
        response = self._transport.get("/api/workers/me", self.api_key)
        if not isinstance(response, dict) or "name" not in response:
            raise WorkerProtocolError("identity response did not name a worker")

        self.name = str(response["name"])

        # Fail loudly on a name mismatch: the key is valid, but it routes to a different worker, so
        # this process would sit idle claiming nothing while the author debugs the wrong thing.
        if self.expected_name and self.expected_name != self.name:
            raise WorkerConfigError(
                f"this key belongs to worker '{self.name}', not '{self.expected_name}'. "
                "Either point the client at the right worker or provision this name separately."
            )

        logger.info("authenticated as worker '%s'", self.name)
        return self.name

    def heartbeat(self) -> None:
        """Tell the server this worker is alive, and report what it is.

        This is also the only place the client can describe itself, since there is no registration
        call: the runtime and subjects go out here and are shown in the admin worker list.
        """
        with self._in_flight_lock:
            in_flight = self._in_flight
        self._transport.post(
            "/api/workers/heartbeat",
            {
                "runtime": self.runtime,
                "subjects": self.subjects,
                "inFlight": in_flight,
            },
            self.api_key,
        )

    def _safe_claim(self, max_items: int) -> list[Task]:
        """Claim without letting a transport failure end the worker.

        The engine keeps the request queued, so a worker that dies on a flaky network loses
        nothing except availability. Retrying is always the right answer.
        """
        try:
            return self._claim(max_items)
        except WorkerProtocolError as error:
            logger.warning("claim failed: %s", error)
            return []

    def run(self, *, install_signal_handlers: bool = True) -> None:
        """Verify the key, then claim and run tasks until stopped.

        Blocks the calling thread. Ctrl-C stops claiming, waits for claimed tasks to finish within
        `shutdown_grace`, and exits. A task that does not finish in time is left to the engine's
        lease recovery, which requeues it.
        """
        if not self._handlers:
            raise WorkerConfigError(
                "the worker has no task handlers; decorate at least one with @client.task(...)"
            )

        self.verify()

        if install_signal_handlers:
            self._install_signal_handlers()

        last_heartbeat = 0.0
        futures: list[Future] = []

        try:
            with ThreadPoolExecutor(max_workers=self.concurrency, thread_name_prefix="argent-task") as pool:
                while not self._stopping.is_set():
                    now = time.monotonic()
                    if now - last_heartbeat >= self.heartbeat_interval:
                        self._safe_heartbeat()
                        last_heartbeat = now

                    futures = [f for f in futures if not f.done()]
                    slots = self.concurrency - self._in_flight

                    if slots > 0:
                        for task in self._safe_claim(min(slots, self.concurrency)):
                            futures.append(pool.submit(self._execute, task))

                    self._stopping.wait(self.poll_interval)
        finally:
            self._drain(futures)

        logger.info("worker '%s' stopped", self.name)

    def stop(self) -> None:
        """Ask the run loop to finish its current work and exit."""
        self._stopping.set()

    # ── Single-task operations, exposed for tests and unusual loops ──────

    def claim(self, max_items: int = 1) -> list[Task]:
        return self._claim(max_items)

    def renew(self, request_id: str, seconds: int | None = None) -> bool:
        """Extend the lease on a claimed request."""
        payload: dict[str, Any] = {}
        if seconds is not None:
            payload["leaseSeconds"] = seconds

        try:
            self._transport.post(f"/api/workers/requests/{request_id}/renew", payload, self.api_key)
            return True
        except WorkerProtocolError as error:
            # A lost lease is a normal outcome, not a crash: the engine will requeue the request.
            logger.warning("could not renew lease on %s: %s", request_id, error)
            return False

    def complete(self, task: Task, succeeded: bool, outputs: dict[str, str] | None, error: str | None) -> None:
        """Report a terminal result for a task."""
        payload = {"succeeded": succeeded, "outputs": outputs or {}}
        if error:
            payload["error"] = error
        self._transport.post(f"/api/workers/requests/{task.id}/complete", payload, self.api_key)

    def status(self, request_id: str) -> dict[str, Any]:
        """Read back a request's state, for a worker that lost its completion response."""
        response = self._transport.get(f"/api/workers/requests/{request_id}", self.api_key)
        if not isinstance(response, dict):
            raise WorkerProtocolError("unexpected status response")
        return response

    # ── Internals ────────────────────────────────────────────────────────

    def _claim(self, max_items: int) -> list[Task]:
        response = self._transport.post(
            "/api/workers/requests/claim",
            {"maxItems": max_items},
            self.api_key,
        )

        if response is None:
            return []
        if not isinstance(response, list):
            raise WorkerProtocolError("claim did not return a list")

        tasks: list[Task] = []
        for item in response:
            if not isinstance(item, dict):
                continue
            tasks.append(
                Task(
                    id=str(item.get("id", "")),
                    subject=str(item.get("subject", "")),
                    parameters=dict(item.get("parameters") or {}),
                    instance_id=str(item.get("instanceId", "")),
                    token_id=str(item.get("tokenId", "")),
                    node_id=str(item.get("nodeId", "")),
                    attempt=int(item.get("attempt", 1)),
                    max_attempts=int(item.get("maxAttempts", 1)),
                    timeout_seconds=int(item.get("timeoutSeconds", 900)),
                    lease_expires_at=str(item.get("leaseExpiresAt", "")),
                    _client=self,
                )
            )
        return tasks

    def _execute(self, task: Task) -> None:
        with self._in_flight_lock:
            self._in_flight += 1

        renewer = _LeaseRenewer(self, task, self.heartbeat_interval)
        try:
            handler = self._handlers.get(task.subject)
            if handler is None:
                # The engine routes by worker name only, so an unhandled subject is possible.
                # Fail loudly rather than holding the lease until it expires.
                self._report_failure(
                    task, f"worker '{self.name}' has no handler for subject '{task.subject}'", None
                )
                return

            renewer.start()
            task.log("running %s", task.subject)

            try:
                result = handler(task)
                outputs = _as_outputs(result)
            except TaskFailure as failure:
                self._report_failure(task, failure.message, failure.outputs)
                return
            except Exception as error:  # noqa: BLE001 - any handler fault is a task failure
                self._report_failure(task, f"{type(error).__name__}: {error}", None)
                return

            # A lost completion response is not fatal: the engine times the request out and
            # requeues it, so the worker keeps running rather than dying on a flaky network.
            try:
                self.complete(task, True, outputs, None)
            except WorkerProtocolError as error:
                logger.error("could not report success for %s: %s", task.id, error)
                return

            task.log("completed")
        finally:
            renewer.stop()
            with self._in_flight_lock:
                self._in_flight -= 1

    def _report_failure(self, task: Task, message: str, outputs: dict[str, str] | None) -> None:
        task.log("failed: %s", message)
        try:
            self.complete(task, False, outputs, message)
        except WorkerProtocolError as error:
            # The engine will time the request out and requeue it, so this is recoverable.
            logger.error("could not report failure for %s: %s", task.id, error)

    def _safe_heartbeat(self) -> None:
        try:
            self.heartbeat()
        except WorkerProtocolError as error:
            logger.warning("heartbeat failed: %s", error)

    def _drain(self, futures: list[Future]) -> None:
        outstanding = [f for f in futures if not f.done()]
        if not outstanding:
            return

        logger.info("waiting up to %.0fs for %d in-flight task(s)", self.shutdown_grace, len(outstanding))
        _, not_done = wait(outstanding, timeout=self.shutdown_grace)

        if not_done:
            # Abandoned tasks are not lost: the lease lapses and the engine requeues the request.
            logger.warning("%d task(s) did not finish in time; the engine will requeue them", len(not_done))

    def _install_signal_handlers(self) -> None:
        def handle(signum: int, _frame: FrameType | None) -> None:
            logger.info("received signal %s; finishing in-flight work", signum)
            self._stopping.set()

        for sig in (signal.SIGINT, signal.SIGTERM):
            try:
                signal.signal(sig, handle)
            except (ValueError, OSError):
                # Signal handlers can only be installed on the main thread; a worker embedded in
                # a larger application manages its own shutdown instead.
                pass


class _LeaseRenewer:
    """Keeps a claimed task's lease alive while its handler runs."""

    def __init__(self, client: WorkerClient, task: Task, interval: float) -> None:
        self._client = client
        self._task = task
        self._interval = interval
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        self._thread = threading.Thread(
            target=self._loop,
            name=f"argent-lease-{self._task.id[:8]}",
            daemon=True,
        )
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)

    def _loop(self) -> None:
        while not self._stop.wait(self._interval):
            self._client.renew(self._task.id)


def _as_outputs(result: Any) -> dict[str, str]:
    """Normalise a handler's return value into the string map the protocol carries."""
    if result is None:
        return {}
    if not isinstance(result, dict):
        raise TaskFailure(f"task handler returned {type(result).__name__}; expected a dict of outputs")

    outputs: dict[str, str] = {}
    for key, value in result.items():
        outputs[str(key)] = "" if value is None else str(value)
    return outputs
