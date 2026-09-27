"""A fake transport that records calls and replays scripted responses.

The client suite drives the whole claim/dispatch/complete loop without a running Argent, which is
what makes the protocol behaviour testable in isolation.
"""

from __future__ import annotations

import json
from typing import Any

from argent_worker.transport import Transport


class FakeTransport:
    def __init__(self, worker_name: str = "reports", status: str = "Idle") -> None:
        self.calls: list[tuple[str, str, Any]] = []
        self.queues: dict[str, list[dict[str, Any]]] = {}
        self.completions: list[dict[str, Any]] = []
        self.renewals: list[str] = []
        self.heartbeats: list[dict[str, Any]] = []
        self.api_key = "test-key"
        self.worker_name = worker_name
        self.status = status
        self.fail_next_complete = False

    def queue(self, *tasks: dict[str, Any]) -> "FakeTransport":
        self.queues.setdefault("tasks", []).extend(tasks)
        return self

    def post(self, path: str, payload: dict[str, Any], api_key: str | None) -> Any:
        self.calls.append(("POST", path, payload))

        if path == "/api/workers/heartbeat":
            self.heartbeats.append(payload)
            return {
                "status": self.status,
                "name": self.worker_name,
                "concurrency": 4,
                "serverTimeUtc": "2026-01-01T00:00:00Z",
            }

        if path == "/api/workers/requests/claim":
            pending = self.queues.get("tasks", [])
            take = min(int(payload.get("maxItems", 1)), len(pending))
            return [pending.pop(0) for _ in range(take)]

        if path.endswith("/renew"):
            request_id = path.split("/")[-2]
            self.renewals.append(request_id)
            return {"id": request_id}

        if path.endswith("/complete"):
            if self.fail_next_complete:
                self.fail_next_complete = False
                from argent_worker.errors import WorkerProtocolError

                raise WorkerProtocolError("POST /complete failed: 500 boom")
            request_id = path.split("/")[-2]
            self.completions.append({"id": request_id, **payload})
            return {"id": request_id, "state": "Succeeded" if payload["succeeded"] else "Failed"}

        raise AssertionError(f"unexpected POST {path}")

    def get(self, path: str, api_key: str | None) -> Any:
        self.calls.append(("GET", path, None))

        if path == "/api/workers/me":
            return {
                "workerId": "worker-1",
                "name": self.worker_name,
                "status": self.status,
                "concurrency": 4,
                "runtime": "Argent Python Worker 0.1.0",
                "subjects": ["render"],
            }

        return {"id": path.split("/")[-1], "state": "Succeeded", "attempt": 1}


def make_task(
    request_id: str = "req-1",
    subject: str = "render",
    parameters: dict[str, str] | None = None,
    **overrides: Any,
) -> dict[str, Any]:
    task = {
        "id": request_id,
        "instanceId": "instance-1",
        "tokenId": "token-1",
        "nodeId": "node-1",
        "workerName": "reports",
        "subject": subject,
        "parameters": parameters or {},
        "attempt": 1,
        "maxAttempts": 2,
        "timeoutSeconds": 900,
        "leaseExpiresAt": "2026-01-01T00:10:00Z",
    }
    task.update(overrides)
    return task


def dumps(value: Any) -> str:
    """Compact JSON, for readable assertion failures."""
    return json.dumps(value, sort_keys=True)
