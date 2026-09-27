class WorkerError(Exception):
    """Base class for every error raised by this client."""


class WorkerConfigError(WorkerError):
    """The client is misconfigured, for example a task handler for a subject it never registered."""


class WorkerProtocolError(WorkerError):
    """The server returned something the client cannot interpret."""


class TaskFailure(Exception):
    """Raise (or return) this from a handler to report a task failure to the workflow.

    A failure is a deterministic result: the engine fails the node rather than retrying it, so
    only raise this for input the worker understood and rejected, not for a transient fault.
    For a transient fault, let the exception propagate; the client reports the task as failed
    either way, so use this when the message matters to the workflow author.

        @client.task("charge")
        def charge(task):
            if not task.parameters.get("card"):
                raise TaskFailure("no card supplied")
            ...
    """

    def __init__(self, message: str, outputs: dict[str, str] | None = None) -> None:
        super().__init__(message)
        self.message = message
        self.outputs = outputs or {}
