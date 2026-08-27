# UI High-Fidelity Design QA

- source visual truth path: `C:\Users\Wan\AppData\Local\Temp\codex-clipboard-a2357979-1d20-40a8-b6c1-c4d289e2d8f9.png`
- implementation screenshot path: `D:\Work\VideoMonitor\.worktrees\wpf-video-monitor-ui\artifacts\screenshots\wpf-video-monitor.png`
- secondary screenshot path: `D:\Work\VideoMonitor\.worktrees\wpf-video-monitor-ui\artifacts\screenshots\wpf-secondary-monitor.png`
- viewport: main window uses primary working area, captured at 1920×1032; secondary window 1440×540
- source pixels: 1672×941; implementation pixels: 1920×1032; secondary pixels: 1440×540
- density normalization: full comparison scales both main captures to 960×540 panels; focused comparisons normalize matching vertical regions to equal panel sizes
- state: default 备用1 + Z-1#巷 + 2#主溜井, detail panel expanded, dark theme

## Evidence

- full view: `artifacts/screenshots/qa-full-comparison.png`
- chrome/tree focus: `artifacts/screenshots/qa-focus-chrome-tree.png`
- detail/status focus: `artifacts/screenshots/qa-focus-detail-status.png`
- focused regions were required because the title-bar controls, tree hierarchy, detail typography, and 36px status bar are too small for reliable inspection in the full-view comparison.

## Required fidelity surfaces

- Fonts and typography: Microsoft YaHei UI, 11–17px hierarchy, semibold video titles, compact auxiliary text. No clipping or unintended wrapping observed at 1920×1032.
- Spacing and layout rhythm: 56px title bar, 200px navigation, 300px tree, fixed 2×2 video grid, 132px detail panel, and 36px status bar match the requested density. Secondary remains one row, three equal columns, total height 540px.
- Colors and tokens: all visible WPF surfaces use the centralized requested palette; no page-local hex colors remain.
- Image quality and asset fidelity: this phase intentionally retains mock video placeholders and does not integrate real streams. All UI icons are centralized WPF Geometry/Path resources rather than Unicode glyphs.
- Copy and content: required station, group, channel, status, detail, and system-metric labels are present. Mock values are clearly presentation-only.

## Comparison history

### Iteration 1 — blocked

- P1: native white Windows title bar and disconnected internal header.
- P1: VideoTile used a 48px header and separate 38px footer, reducing the video area.
- P1: detail panel and complete system status bar were missing.
- P2: navigation/tree hierarchy was visually flat and used Unicode character icons.
- Fixes: added WindowChrome, strict palette/density tokens, Geometry icons, compact overlay-based VideoTile, selected tree rows/search/status, collapsible detail panel, complete status bars, and custom secondary chrome.

### Iteration 2 — passed

- Post-fix evidence: the full comparison shows matching industrial dark hierarchy and dominant video area; focused comparisons confirm integrated chrome, compact typography, right-tree density, detail panel, and bottom status rhythm.
- No actionable P0/P1/P2 differences remain within this mock-UI-only phase.

### Iteration 3 — passed

- Follow-up finding: the original 34px collapsed row did not include the panel's 10px vertical margin, leaving only 24px for a 34px header and clipping its content.
- Fix: collapsed row height is now 44px (34px header + 10px vertical margin). Runtime evidence: `artifacts/screenshots/wpf-detail-collapsed-fixed.png`.

### Iteration 4 — passed

- Follow-up finding: the detail labels and bold values used separate TextBlocks with different font sizes, producing a visible baseline offset.
- Fix: each label/value pair now shares one TextBlock with inline Runs. Runtime evidence: `artifacts/screenshots/wpf-detail-header-aligned.png`.

## Follow-up polish (P3, acceptance-dependent)

- Target uses real mine imagery and mixed error/offline states; implementation intentionally keeps neutral mock video placeholders and existing all-online fake data.
- Target contains a richer left-side monitor-group mini tree and more toolbar actions; implementation keeps the explicitly requested navigation/favorites scope and avoids adding new behavior.
- Detail panel omits target-only thumbnail, vendor, codec, account, and storage tabs because the requested minimum fields are already present and this phase forbids feature expansion.

## Interaction verification

- Custom title-bar minimize/maximize/close controls render and remain hit-testable.
- Main four-view fullscreen changes the window from 1920×1032 to 1920×1080; Esc restores 1920×1032.
- Secondary remains 1440×540 in the single-monitor development setup, with a single three-column row.

final result: passed
