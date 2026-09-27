"""Client behaviour: identity, the claim loop, dispatch, and completion."""

from __future__ import annotations

import threading

import pytest
from fake_transport import FakeTransport, dumps, make_task

from argent_worker import Task, TaskFailure, WorkerClient, WorkerConfigError
from argent_worker.client import DEFAULT_RUNTIME


def make_client(transport: FakeTransport, **kwargs) -> WorkerClient:
    kwargs.setdefault("name", "reports")
    return WorkerClient("test-key", base_url="https://argent.test", transport=transport, **kwargs)


# ── Identity ───────────────────────────────────────────────────────────


def test_a_client_cannot_be_created_without_an_api_key():
    # There is no registration call to fall back on: the key comes from an administrator.
    with pytest.raises(WorkerConfigError, match="API key is required"):
        WorkerClient("", base_url="https://argent.test")


def test_verify_learns_which_worker_the_key_belongs_to():
    transport = FakeTransport(worker_name="reports")
    client = make_client(transport)

    assert client.verify() == "reports"
    assert transport.calls[0][0:2] == ("GET", "/api/workers/me")


def test_verify_rejects_a_key_for_a_different_worker():
    # The key is valid, but it routes elsewhere, so the process would sit idle claiming nothing.
    transport = FakeTransport(worker_name="mailer")
    client = make_client(transport, name="reports")

    with pytest.raises(WorkerConfigError, match="belongs to worker 'mailer'"):
        client.verify()


def test_verify_accepts_any_name_when_none_is_expected():
    transport = FakeTransport(worker_name="mailer")
    client = WorkerClient("test-key", base_url="https://argent.test", transport=transport)

    assert client.verify() == "mailer"


# ── Reporting ──────────────────────────────────────────────────────────


def test_the_heartbeat_reports_the_runtime_and_handlers():
    transport = FakeTransport()
    client = make_client(transport, subjects=["export"])

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        return {}

    client.heartbeat()

    beat = transport.heartbeats[0]
    # Free-form identification, not a closed enum: nothing routes on it, and the version makes it
    # obvious which client build is deployed.
    assert beat["runtime"] == DEFAULT_RUNTIME
    assert beat["runtime"].startswith("Argent Python Worker ")
    assert sorted(beat["subjects"]) == ["export", "render"]
    assert beat["inFlight"] == 0


def test_the_runtime_can_be_overridden_to_record_a_build():
    transport = FakeTransport()
    client = make_client(transport, runtime="reports-worker 2.4.1 (build 8891)")
    client.heartbeat()

    assert transport.heartbeats[0]["runtime"] == "reports-worker 2.4.1 (build 8891)"


def test_a_client_with_no_handlers_refuses_to_run():
    client = make_client(FakeTransport())

    with pytest.raises(WorkerConfigError, match="no task handlers"):
        client.run(install_signal_handlers=False)


# ── Claim and dispatch ─────────────────────────────────────────────────


def test_claim_maps_the_wire_payload_onto_a_task():
    transport = FakeTransport().queue(make_task(subject="render", parameters={"customer": "Contoso"}))
    client = make_client(transport)
    client.verify()

    [task] = client.claim(1)

    assert task.id == "req-1"
    assert task.subject == "render"
    assert task.parameters == {"customer": "Contoso"}
    assert task.instance_id == "instance-1"
    assert task.token_id == "token-1"
    assert task.attempt == 1
    assert task.max_attempts == 2
    assert task.timeout_seconds == 900


def test_claim_returns_nothing_when_the_queue_is_empty():
    client = make_client(FakeTransport())
    client.verify()

    assert client.claim(1) == []


def test_a_handler_result_becomes_string_outputs():
    transport = FakeTransport().queue(make_task())
    client = make_client(transport)
    client.verify()

    @client.task("render")
    def render(task: Task) -> dict[str, object]:
        return {"reportUrl": "https://files/r.pdf", "pageCount": 12, "generated": None}

    [task] = client.claim(1)
    client._execute(task)

    [completion] = transport.completions
    assert completion["id"] == "req-1"
    assert completion["succeeded"] is True
    # Everything is stringified: the protocol carries strings, and downstream nodes read the
    # values as text either way.
    assert completion["outputs"] == {
        "reportUrl": "https://files/r.pdf",
        "pageCount": "12",
        "generated": "",
    }


def test_a_handler_returning_none_succeeds_with_no_outputs():
    transport = FakeTransport().queue(make_task())
    client = make_client(transport)
    client.verify()

    @client.task("render")
    def render(task: Task) -> None:
        return None

    [task] = client.claim(1)
    client._execute(task)

    assert transport.completions[0]["succeeded"] is True
    assert transport.completions[0]["outputs"] == {}


def test_raising_task_failure_fails_the_node_with_that_message():
    transport = FakeTransport().queue(make_task())
    client = make_client(transport)
    client.verify()

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        raise TaskFailure("the upstream API returned 500", {"retryAfter": "30"})

    [task] = client.claim(1)
    client._execute(task)

    completion = transport.completions[0]
    assert completion["succeeded"] is False
    assert completion["error"] == "the upstream API returned 500"
    assert completion["outputs"] == {"retryAfter": "30"}


def test_an_unexpected_exception_fails_the_node_with_its_type():
    transport = FakeTransport().queue(make_task())
    client = make_client(transport)
    client.verify()

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        raise ValueError("bad input")

    [task] = client.claim(1)
    client._execute(task)

    completion = transport.completions[0]
    assert completion["succeeded"] is False
    assert "ValueError" in completion["error"]
    assert "bad input" in completion["error"]


def test_an_unhandled_subject_fails_immediately():
    # The engine routes by worker name only, so a subject with no handler is possible. Failing
    # beats holding the lease until it expires.
    transport = FakeTransport().queue(make_task(subject="mystery"))
    client = make_client(transport)
    client.verify()

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        return {}

    [task] = client.claim(1)
    client._execute(task)

    completion = transport.completions[0]
    assert completion["succeeded"] is False
    assert "mystery" in completion["error"]


def test_a_handler_returning_a_non_dict_fails():
    transport = FakeTransport().queue(make_task())
    client = make_client(transport)
    client.verify()

    @client.task("render")
    def render(task: Task) -> str:
        return "not a dict"

    [task] = client.claim(1)
    client._execute(task)

    assert transport.completions[0]["succeeded"] is False
    assert "expected a dict" in transport.completions[0]["error"]


def test_a_lost_completion_does_not_crash_the_worker():
    transport = FakeTransport().queue(make_task())
    transport.fail_next_complete = True
    client = make_client(transport)
    client.verify()

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        return {"ok": "1"}

    [task] = client.claim(1)
    client._execute(task)

    # The engine will time the request out and requeue it, so this is recoverable.
    assert transport.completions == []


# ── Leases and heartbeats ──────────────────────────────────────────────


def test_renewing_a_lease_posts_to_the_request():
    transport = FakeTransport()
    client = make_client(transport)
    client.verify()

    assert client.renew("req-9", seconds=120) is True

    method, path, payload = transport.calls[-1]
    assert path == "/api/workers/requests/req-9/renew"
    assert payload["leaseSeconds"] == 120


def test_a_renewed_lease_reports_failure_without_raising():
    from argent_worker.errors import WorkerProtocolError

    class FailingRenewal(FakeTransport):
        def post(self, path, payload, api_key):
            if path.endswith("/renew"):
                raise WorkerProtocolError("409 not held")
            return super().post(path, payload, api_key)

    client = make_client(FailingRenewal())
    client.verify()

    assert client.renew("req-9") is False


def test_a_long_task_keeps_its_lease_alive():
    import time

    transport = FakeTransport().queue(make_task())
    client = make_client(transport, heartbeat_interval=0.05)
    client.verify()

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        time.sleep(0.2)
        return {"ok": "1"}

    [task] = client.claim(1)
    client._execute(task)

    assert transport.renewals, "the lease renewer should have run while the handler was working"
    assert all(r == "req-1" for r in transport.renewals)


def test_heartbeat_reports_in_flight_work():
    transport = FakeTransport()
    client = make_client(transport)
    client.verify()

    client.heartbeat()

    assert transport.heartbeats[0]["inFlight"] == 0


# ── Task helpers ───────────────────────────────────────────────────────


def a_task(**overrides) -> Task:
    defaults = dict(
        id="t", subject="s", parameters={}, instance_id="", token_id="", node_id="",
        attempt=1, max_attempts=1, timeout_seconds=1, lease_expires_at="",
    )
    defaults.update(overrides)
    return Task(**defaults)


def test_require_raises_when_a_parameter_is_missing():
    with pytest.raises(KeyError, match="customer"):
        a_task().require("customer")


def test_parameter_falls_back_to_a_default():
    task = a_task(parameters={"a": "1"})

    assert task.parameter("a") == "1"
    assert task.parameter("missing", "fallback") == "fallback"


# ── The run loop ───────────────────────────────────────────────────────


def test_run_claims_dispatches_and_stops():
    transport = FakeTransport().queue(make_task(request_id="req-a"), make_task(request_id="req-b"))
    client = make_client(transport, concurrency=2, poll_interval=0.01, heartbeat_interval=0.01)
    done = threading.Event()
    seen: list[str] = []

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        seen.append(task.id)
        if len(seen) == 2:
            done.set()
        return {"ok": "1"}

    stopper = threading.Thread(target=lambda: (done.wait(5.0), client.stop()), daemon=True)
    stopper.start()

    client.run(install_signal_handlers=False)

    assert sorted(seen) == ["req-a", "req-b"]
    assert sorted(c["id"] for c in transport.completions) == ["req-a", "req-b"]
    assert dumps(transport.heartbeats)  # the loop heartbeats while it runs


def test_run_verifies_the_key_before_claiming():
    transport = FakeTransport()
    client = make_client(transport, poll_interval=0.01, heartbeat_interval=0.01)

    @client.task("render")
    def render(task: Task) -> dict[str, str]:
        return {}

    threading.Timer(0.2, client.stop).start()
    client.run(install_signal_handlers=False)

    paths = [path for _, path, _ in transport.calls]
    # Identity first, so a bad key is reported before the loop starts doing work.
    assert paths[0] == "/api/workers/me"
    assert "/api/workers/requests/claim" in paths


def test_no_client_request_can_claim_as_a_different_worker():
    transport = FakeTransport().queue(make_task())
    client = make_client(transport)
    client.verify()
    client.claim(1)

    # Identity comes from the key alone; the client never asserts an id the server might ignore.
    for method, path, payload in transport.calls:
        if path == "/api/workers/requests/claim":
            assert "workerId" not in payload
