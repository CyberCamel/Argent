"""A single unit of work handed to a handler."""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Callable

if TYPE_CHECKING:  # pragma: no cover - import cycle only matters to type checkers
    from .client import WorkerClient

logger = logging.getLogger("argent_worker")


@dataclass(frozen=True)
class Task:
    """One claimed request. The id is stable across delivery attempts and is the idempotency key.

    Attributes:
        id: Stable request id. Persist it if a handler must be safe to run twice.
        subject: The task identifier the workflow author configured on the node.
        parameters: Key/value parameters interpolated by the engine from workflow variables.
        instance_id, token_id, node_id: Workflow coordinates, for correlating worker logs with
            the engine's journal entries.
        attempt: 1 on first delivery, higher if a previous attempt lost its lease.
        max_attempts: How many times the engine will deliver this request.
        timeout_seconds: The author's hard budget. The engine fails the request when it elapses,
            whether or not the lease is being renewed.
        lease_expires_at: When the current lease runs out, after which another worker may take it.
    """

    id: str
    subject: str
    parameters: dict[str, str]
    instance_id: str
    token_id: str
    node_id: str
    attempt: int
    max_attempts: int
    timeout_seconds: int
    lease_expires_at: str

    _client: "WorkerClient | None" = field(default=None, repr=False, compare=False)

    def parameter(self, key: str, default: str | None = None) -> str | None:
        """Read a parameter, falling back when the author did not supply it."""
        value = self.parameters.get(key)
        return default if value is None else value

    def require(self, key: str) -> str:
        """Read a parameter that the task cannot proceed without."""
        value = self.parameters.get(key)
        if value is None or value == "":
            raise KeyError(f"task {self.id} ({self.subject}) requires parameter '{key}'")
        return value

    def log(self, message: str, *args: object) -> None:
        """Log with the request and token ids attached, so worker logs line up with the engine's."""
        logger.info(
            "[%s token=%s] " + message,
            self.id,
            self.token_id,
            *args,
            extra={"argent_task_id": self.id, "argent_token_id": self.token_id},
        )

    def renew_lease(self, seconds: int | None = None) -> bool:
        """Extend this task's lease. A worker holding a lease forever still loses to the
        author's timeout, so a genuinely long task should still finish.
        """
        if self._client is None:
            return False
        return self._client.renew(self.id, seconds)


Handler = Callable[[Task], "dict[str, str] | None"]
