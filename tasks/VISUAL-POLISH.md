# Visual polish contract

Keep the Explorer-style layout and existing commands. Introduce shared semantic colors,
Segoe command icons with a Windows 10 fallback, a 4/8/12/16/24 spacing scale, 34px command
targets, 36px normal / 30px compact rows, and explicit hover/selection/inactive/focus states.
Keep native file/drive icons: they communicate file types and are not action glyphs.

Distinguish folder loading, index loading, empty folder, empty search, and failure. Empty
states must offer a relevant action and remain clickable above an empty table. Delayed old
requests must not end the loading state of a newer request.

At app zoom 75/100/125/150%, keep the native titlebar unscaled, reflow contextual search into
a second row below 760 logical pixels, compact command labels and retain all actions.
Dialogs/popups follow app zoom; scroll dialog content where needed. Use dynamic theme colors
for all custom surfaces, with legible foregrounds in both light and dark themes.

Verify layout/resource/contrast/focus/state behavior without changing the user's saved settings.
Run native smoke when available; distinguish app zoom layout checks from actual multi-monitor
DPI testing. Preserve existing work; no automatic commit.

References reviewed: user-supplied Windows Explorer screenshots; awesome-design-md catalogue;
Microsoft iconography/font and WPF DPI documentation. Fluent-style density remains the direction.


Completed: shared resources/palette, visual states, scaled dialog content, adaptive search row,
24-layout matrix, contrast/selection/hit-test/resize checks, independent review and native smoke.
155 tests passed, clean Release build, package refreshed. See docs/VISUAL-POLISH.md for evidence
and remaining native multi-monitor DPI / assistive-technology checks.
