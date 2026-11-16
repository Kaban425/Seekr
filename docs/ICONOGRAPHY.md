# Seekr icon set

The application's own icons are original vector paths authored in this repository:
`src/Seekr.App/Icons/IconArtwork.cs`. They use a 24-unit grid, rounded 1.65-unit strokes
and a small category palette. They are covered by the project's MIT license.

No Windows shell images, installed-application logos, downloaded icon packs, traced font
outlines or icon font files are used for these assets. Familiar concepts such as arrows,
folders, pages and drives remain recognizable; the drawings are defined directly in code.

## Coverage

- 37 named icons: navigation, toolbar actions, checkbox check, expansion chevron, states,
  sidebar locations, tabs, folder/drive/file categories.
- `SeekrIcon` draws action icons with an inherited theme-aware foreground and does not
  take keyboard focus or intercept pointer input. Existing button automation labels remain.
- `IconArtwork.Image` supplies frozen shared drawings for file rows, tabs and navigation.
  Cache keys are the finite icon categories used by the application, not arbitrary paths
  or extensions. Unknown file extensions reuse one generic image.
- `FilePresentation` replaces shell icon/type lookup. PDF, documents, pictures, music,
  video, archives, source code and applications have Seekr-owned category images.
  Type labels retain format information and identify shortcuts/libraries without registry
  descriptions or branding from installed applications.
- Native window controls and library-provided standard widgets remain standard controls;
  this change does not claim ownership of their implementation or imagery.

## Rights context

Use of a system-installed symbol font is different from extracting and redistributing
Windows graphics. Microsoft's [font FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq)
permits Windows applications to render installed fonts, while its
[copyright guidance](https://www.microsoft.com/en-us/legal/intellectualproperty/copyright/permissions)
sets separate conditions for Microsoft imagery and product icons. These references do not
constitute a legal clearance of every possible use. Seekr's new project-defined artwork
avoids depending on those system icon assets for the application's own icons.

## Verification

- 164 tests passed: 47 Core, 75 Indexer, 42 App. New checks cover path bounds, frozen shared
  images, category mapping, cross-thread access, unknown-extension reuse and type labels.
- The existing light/dark × 75/100/125/150% × 840/1100/1440 matrix was rerun with the new
  icons. Rendered previews and icon atlases are in `artifacts/visual-polish`.
- A source scan found no remaining `SHGetFileInfo`, `ExplorerIconHelper`, `FontIcon`,
  `Seekr.IconFont`, Segoe MDL2 or Segoe Fluent references in the project's own C#/XAML.
- Independent review found no confirmed high/medium regressions in the icon replacement.
- Release build is clean. A separate self-contained bundle is published at
  `artifacts/Seekr-custom-icons/Seekr.App.exe`, with its adjacent service payload, because
  the previously launched `artifacts/Seekr` app was still using its files.
- Preview generation exercised actual WPF rendering; the new bundle was not launched
  alongside the user's already-running copy during this pass.

Previews: `artifacts/visual-polish/seekr-icons-light.png`, `seekr-icons-dark.png`,
`light-100.png`, `dark-100.png`, and `light-150-narrow.png`.
