# Explorer upgrade

- [x] Direct browsing and global ordering
- [x] Shared index and diagnostics
- [x] Sizes and space map
- [x] Transfers, conflicts and queue
- [x] Locks and service setup
- [x] Adaptive columns, integration and verification

Verification: Release build with zero warnings/errors; 120 tests passed (45 core, 58 indexer, 17 app). Self-contained win-x64 package built and --check-package passed. Runtime smoke: direct browsing D: while global index is loading, measured folder sizes, and space-map rendering. No service installation or termination of existing applications was performed during verification.
