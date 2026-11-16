# Reliability and performance pass

Scope: preserve current UI/features while separating search execution from presentation,
fixing cancellation/concurrency/lifetime bugs, hardening the service response path and
measuring index/search/scan costs on reproducible generated data.

Acceptance:
- Concurrent index refresh callers share one load; a transient failure preserves the last
  snapshot and the next refresh can succeed. Shutdown cancels and drains background work.
- Queued and active file jobs cancel independently, including a pending conflict response;
  one failed job does not block the next; completed task history is bounded.
- Cancelled/superseded searches cannot publish stale results or errors.
- Service permission filtering and serialization honor cancellation; response serialization
  uses bounded buffering. Existing identity verification and pipe ACLs remain enforced.
- Cache publication is atomic under cancellation/concurrent saves and leaves no temp files.
- A repeatable benchmark records latency distributions, allocations, managed heap and process
  memory, including fixture/machine/runtime details. Results do not imply a whole-app benchmark.
- Release build/tests, dependency audit, review, report and ready-to-run package.

No service installation, elevation, termination of user applications or operations on user
files during verification. Existing uncommitted work is preserved; no automatic commits.


Completed implementation and verification: see docs/RELIABILITY-REPORT.md for measurements,
153 passing tests, runtime smoke and remaining release-validation limitations. No claim of
complete production readiness or recovery from OS/power failure. Final independent re-review
was unavailable due to the reviewing agent's usage limit; initial security review and local
self-review/tests were completed.
