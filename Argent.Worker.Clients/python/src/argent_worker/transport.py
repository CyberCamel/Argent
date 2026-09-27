"""HTTP transport for the Argent worker API.

Uses only the standard library so the client can be dropped into any Python environment without
a dependency install. Tests inject a different transport rather than reaching the network.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from typing import Any, Protocol

from .errors import WorkerProtocolError

DEFAULT_TIMEOUT = 30.0


class Transport(Protocol):
    """The HTTP surface the client depends on."""

    def post(self, path: str, payload: dict[str, Any], api_key: str | None) -> Any: ...

    def get(self, path: str, api_key: str | None) -> Any: ...


class HttpTransport:
    def __init__(self, base_url: str, timeout: float = DEFAULT_TIMEOUT) -> None:
        self._base_url = base_url.rstrip("/")
        self._timeout = timeout

    def post(self, path: str, payload: dict[str, Any], api_key: str | None) -> Any:
        return self._send("POST", path, payload, api_key)

    def get(self, path: str, api_key: str | None) -> Any:
        return self._send("GET", path, None, api_key)

    def _send(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None,
        api_key: str | None,
    ) -> Any:
        body = None if payload is None else json.dumps(payload).encode("utf-8")

        request = urllib.request.Request(f"{self._base_url}{path}", data=body, method=method)
        request.add_header("Accept", "application/json")
        if body is not None:
            request.add_header("Content-Type", "application/json")
        if api_key:
            request.add_header("Authorization", f"Bearer {api_key}")

        try:
            with urllib.request.urlopen(request, timeout=self._timeout) as response:
                raw = response.read()
        except urllib.error.HTTPError as error:
            # 4xx carries a JSON error body worth surfacing; a 5xx does not help the worker decide.
            detail = _error_detail(error)
            raise WorkerProtocolError(f"{method} {path} failed: {error.code} {detail}") from error
        except urllib.error.URLError as error:
            raise WorkerProtocolError(f"{method} {path} could not reach {self._base_url}: {error.reason}") from error

        if not raw:
            return None

        try:
            return json.loads(raw)
        except json.JSONDecodeError as error:
            raise WorkerProtocolError(f"{method} {path} returned a body that is not JSON") from error


def _error_detail(error: urllib.error.HTTPError) -> str:
    try:
        payload = json.loads(error.read())
    except Exception:  # noqa: BLE001 - the body is best effort context for an error message
        return error.reason or ""

    if isinstance(payload, dict) and "error" in payload:
        return str(payload["error"])
    return str(payload)
