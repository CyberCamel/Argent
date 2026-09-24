# Argent Platform Hardening Backlog

This document collects work deliberately deferred until the end-to-end MVP is operational.
Items here are not considered implemented merely because a protocol seam or placeholder exists.

## Forms and attachments

- Implement direct or chunked attachment uploads. Define storage provider, size/type limits,
  malware scanning, retention, authorization, download auditing, and orphan cleanup.
- Add interrupted-upload recovery and ensure raw file bytes never enter canonical form state.
- Add searchable dependent reference lookups with cancellation, debounce, paging, and an
  accessible loading/error state. MVP references intentionally load a bounded/simple option set.
- Add multi-reference controls and collection-aware validation.
- Add draft autosave, recovery, and conflict handling.
- Handle expired sessions and external/eID redirect recovery without losing entered values.
- Add richer controls and presentations, including radio groups.
- Add property-based and differential protocol tests for Unicode, decimals, dates, nulls,
  nesting, and invalid definitions.

## Reliability and security

- Review authorization for every designer/runtime data endpoint and remove temporary bypasses.
- Add rate limiting and abuse controls to anonymous form bootstrap, lookup, upload, and submit.
- Add transactional cleanup/reconciliation for submissions that cross record, workflow, and
  attachment persistence boundaries.
- Define retention, redaction, and audit policies for citizen-submitted data.
- Resolve package vulnerability warnings and automate dependency auditing.

## Performance and operations

- Measure cold-cache transfer, first usable field, interaction latency, and layout shift on a
  throttled mid-range mobile profile.
- Exercise a 150-field dependency-heavy form and enforce the documented performance budgets.
- Add runtime telemetry for bootstrap, validation, lookup, submission, and workflow-start errors.
- Add production health checks and recovery diagnostics for the separate workflow engine.

## Accessibility and localization

- Run WCAG 2.2 AA automated tests plus keyboard and screen-reader reviews.
- Review authored labels, descriptions, validation fallbacks, and workflow action names for
  localization strategy and right-to-left support.
