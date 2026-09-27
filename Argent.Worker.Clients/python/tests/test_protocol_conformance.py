"""Conformance: the payloads the Python client sends and expects must match the published schema.

`schemas/argent-worker-protocol.schema.json` is the single source of truth shared by every
worker client. Checking the client's own request and response shapes against it here is what stops
the clients drifting apart as the protocol changes.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest
from fake_transport import FakeTransport, make_task

from argent_worker import WorkerClient
from argent_worker.transport import HttpTransport

SCHEMA_PATH = Path(__file__).resolve().parents[3] / "schemas" / "argent-worker-protocol.schema.json"


@pytest.fixture(scope="module")
def schema() -> dict[str, Any]:
    return json.loads(SCHEMA_PATH.read_text())


@pytest.fixture(scope="module")
def validate(schema):
    """Validate an instance against one `$defs` entry of the published schema."""

    def check(instance: Any, ref: str) -> None:
        jsonschema = pytest.importorskip("jsonschema")
        # Root plus an explicit $ref keeps $defs resolvable in the same resource.
        validator = jsonschema.Draft202012Validator({**schema, "$ref": ref})
        errors = sorted(validator.iter_errors(instance), key=lambda e: list(e.absolute_path))
        assert not errors, "\n".join(f"{list(e.absolute_path)}: {e.message}" for e in errors)

    return check


def ready_client(transport: FakeTransport, **kwargs) -> WorkerClient:
    client = WorkerClient("test-key", base_url="https://argent.test", transport=transport, **kwargs)
    client.verify()
    return client


def test_the_schema_is_published():
    assert SCHEMA_PATH.exists(), "the worker protocol schema must ship with the repository"
    assert SCHEMA_PATH.name == "argent-worker-protocol.schema.json"


def test_every_endpoint_is_documented(schema):
    endpoints = {(e["method"], e["path"]) for e in schema["x-endpoints"]}

    assert endpoints == {
        ("GET", "/api/workers/me"),
        ("POST", "/api/workers/heartbeat"),
        ("POST", "/api/workers/requests/claim"),
        ("POST", "/api/workers/requests/{id}/renew"),
        ("POST", "/api/workers/requests/{id}/complete"),
        ("GET", "/api/workers/requests/{id}"),
    }


def test_the_schema_offers_no_registration_endpoint(schema):
    paths = {e["path"] for e in schema["x-endpoints"]}

    # Workers are provisioned by an administrator. A client that could register itself would be
    # able to take over any worker name, so the endpoint must not exist.
    assert not any("register" in p for p in paths)


def test_the_runtime_is_a_free_form_string_not_an_enum(schema):
    runtime = schema["$defs"]["heartbeatRequest"]["properties"]["runtime"]

    assert "enum" not in runtime, "the runtime must stay open-ended so clients can version themselves"
    assert runtime["maxLength"] == 256


def test_identity_response_matches_the_schema(validate):
    transport = FakeTransport()
    ready_client(transport)

    validate(transport.get("/api/workers/me", "test-key"), "#/$defs/identityResponse")


def test_heartbeat_request_matches_the_schema(validate):
    transport = FakeTransport()
    client = ready_client(transport, subjects=["export"])

    @client.task("render")
    def render(task) -> dict[str, str]:
        return {}

    client.heartbeat()

    _, path, payload = transport.calls[-1]
    assert path == "/api/workers/heartbeat"
    validate(payload, "#/$defs/heartbeatRequest")


def test_heartbeat_response_matches_the_schema(validate):
    transport = FakeTransport()
    client = ready_client(transport)
    client.heartbeat()

    validate(transport.post("/api/workers/heartbeat", {"inFlight": 0}, "test-key"),
             "#/$defs/heartbeatResponse")


def test_a_claim_request_matches_the_schema(validate):
    transport = FakeTransport()
    client = ready_client(transport)
    client.claim(2)

    _, path, payload = transport.calls[-1]
    assert path == "/api/workers/requests/claim"
    validate(payload, "#/$defs/claimRequest")


def test_a_claimed_task_matches_the_schema(validate):
    transport = FakeTransport().queue(make_task(parameters={"customer": "Contoso"}))
    client = ready_client(transport)

    [task] = client.claim(1)
    validate(
        {
            "id": task.id,
            "instanceId": task.instance_id,
            "tokenId": task.token_id,
            "nodeId": task.node_id,
            "workerName": "reports",
            "subject": task.subject,
            "parameters": task.parameters,
            "attempt": task.attempt,
            "maxAttempts": task.max_attempts,
            "timeoutSeconds": task.timeout_seconds,
            "leaseExpiresAt": task.lease_expires_at,
        },
        "#/$defs/task",
    )


def test_completion_request_matches_the_schema(validate):
    transport = FakeTransport().queue(make_task())
    client = ready_client(transport)

    [task] = client.claim(1)
    client.complete(task, True, {"reportUrl": "https://files/r.pdf"}, None)
    client.complete(task, False, None, "upstream returned 500")

    for completion in transport.completions:
        validate(completion, "#/$defs/completeRequest")


def test_a_success_without_outputs_is_still_a_valid_completion(validate):
    transport = FakeTransport().queue(make_task())
    client = ready_client(transport)

    [task] = client.claim(1)
    client.complete(task, True, None, None)

    validate(transport.completions[0], "#/$defs/completeRequest")


def test_lease_renewal_matches_the_schema(validate):
    transport = FakeTransport()
    client = ready_client(transport)

    client.renew("req-1", seconds=120)
    validate(transport.calls[-1][2], "#/$defs/renewLeaseRequest")


def test_the_transport_normalises_the_base_url():
    assert HttpTransport("https://argent.test/")._base_url == "https://argent.test"
