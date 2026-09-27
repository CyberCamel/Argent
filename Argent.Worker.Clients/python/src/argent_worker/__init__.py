"""Run Argent workflow tasks from a Python process.

The engine never learns what the work is. It sends a worker name, a subject string, and a set of
key/value parameters; the worker decides which handler runs and what it returns. Outputs the
handler returns become workflow process variables.

    import os

    from argent_worker import WorkerClient

    client = WorkerClient(
        api_key=os.environ["ARGENT_WORKER_API_KEY"],
        base_url="https://localhost:5001",
        name="reports",
    )

    @client.task("render")
    def render(task):
        return {"reportUrl": build_pdf(task.parameters["customer"]).url}

    client.run()

The worker is provisioned by an administrator under Admin -> Workers, who is shown the API key
once. The client cannot register itself and has no way to obtain a key.
"""

from ._version import __version__
from .client import WorkerClient
from .errors import TaskFailure, WorkerConfigError, WorkerProtocolError
from .task import Task

__all__ = [
    "Task",
    "TaskFailure",
    "WorkerClient",
    "WorkerConfigError",
    "WorkerProtocolError",
    "__version__",
]
