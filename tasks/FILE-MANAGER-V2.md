# File manager v2 — scope and acceptance

User authorized implementation of the existing-functionality pass plus three new capabilities.

| Module | Acceptance | Dependencies |
| --- | --- | --- |
| transfers | Atomic publication for cross-volume moves; preserve source on failure; shared filename validation | core |
| live-search | Changed scoped folders use current data instead of stale cached index; cancellation and bounds | existing search |
| size-cache | Reuse measured sizes briefly; invalidate on changes/operations/manual rescan; mark indexed estimates | existing sizes |
| saved-searches | Save content/name mode, scope and ordering; rename/update/remove with validation and isolated tests | existing settings |
| dual-pane | Independent paths; explicit active pane; copy/move selected roots to other pane; shared queue | transfers |
| batch-rename | Preview before applying; one parent; collision/reserved-name checks; staging, rollback and session undo | filename validation, queue |
| content-search | Local bounded/cancellable readers, filename metadata filters plus terms in content, visible partial limits | live-search |

Build order: core contracts/tests → existing features → dual panes → rename UI → content UI →
integration, review, tests, self-contained bundle. Preserve all prior uncommitted work.

Content search starts with text/source files; evaluate maintained PDF parser and built-in DOCX
ZIP/XML support. Never silently treat unsupported/binary/oversized/unreadable files as inspected.
No cloud, no service install and no destructive actions on user files during tests. All file-changing
tests use isolated fixtures. Graceful cancellation is not a guarantee of crash/power-loss recovery.

Implemented and verified: all seven modules. See docs/FILE-MANAGER-V2.md for usage,
176-test evidence and explicit boundaries. PDF evaluated and excluded pending process isolation;
TXT/source/DOCX content search ships without a new dependency. Native manual/cross-volume
smoke remains separate from the passing isolated tests and offscreen layout verification.
