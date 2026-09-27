"""A runnable example worker.

Provision it first: in Argent, open Admin -> Workers, provision a worker named "reports", and
copy the API key it shows. Then:

    export ARGENT_WORKER_API_KEY=<the key>
    python examples/report_worker.py

The workflow it serves looks like this: a Worker Activity node targeting worker "reports",
subject "render", with parameters customer and format. Configure the node to match, or change
the subject below.
"""

from __future__ import annotations

import logging
import os
import sys

from argent_worker import Task, TaskFailure, WorkerClient

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger("example")


def build_report(customer: str, report_format: str) -> dict[str, str]:
    """Stand-in for whatever the task actually does."""
    logger.info("building a %s report for %s", report_format, customer)
    return {
        "reportUrl": f"https://files.example.com/reports/{customer}.{report_format}",
        "pageCount": "12",
    }


client = WorkerClient(
    # The key is issued by an administrator under Admin -> Workers and shown exactly once.
    # Read it from the environment; never commit it.
    api_key=os.environ["ARGENT_WORKER_API_KEY"],
    base_url=os.environ.get("ARGENT_BASE_URL", "https://localhost:5001"),
    name=os.environ.get("ARGENT_WORKER_NAME", "reports"),
    concurrency=int(os.environ.get("ARGENT_WORKER_CONCURRENCY", "2")),
)


@client.task("render")
def render(task: Task) -> dict[str, str]:
    customer = task.require("customer")
    report_format = task.parameters.get("format", "pdf")

    if not customer.strip():
        raise TaskFailure("customer is blank")

    outputs = build_report(customer, report_format)

    # task.log carries the request and token ids, so these lines line up with the engine's
    # journal entries for the same request.
    task.log("rendered %s report for %s", report_format, customer)
    return outputs


if __name__ == "__main__":
    if "--help" in sys.argv:
        print(__doc__)
        sys.exit(0)

    logger.info("starting against %s", client.base_url)
    client.run()
