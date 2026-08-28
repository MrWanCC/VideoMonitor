# UI Scale Pass 2 Design QA

## Comparison Target

- source visual truth path: `D:\Work\VideoMonitor\.worktrees\wpf-video-monitor-ui\artifacts\screenshots\target-effect-archived.png`
- source provenance: recovered from the left side of the repository's prior `qa-full-comparison.png`, which used the user-confirmed target screenshot. The original temporary attachment had already been removed by Windows.
- implementation screenshot path: `D:\Work\VideoMonitor\.worktrees\wpf-video-monitor-ui\artifacts\screenshots\wpf-video-monitor-ui-scale-pass2-main.png`
- secondary screenshot path: `D:\Work\VideoMonitor\.worktrees\wpf-video-monitor-ui\artifacts\screenshots\wpf-video-monitor-ui-scale-pass2-secondary.png`
- viewport: main window captured at 1920×1080; secondary window captured at 1440×540.
- source pixels: archived target 960×540, normalized from the original 16:9 target capture in the prior comparison artifact.
- implementation pixels: 1920×1080 native capture, downsampled to 960×540 for equal-size comparison.
- CSS size / device scale: not applicable to WPF. Runtime capture used the local 1920×1080 display at the Windows desktop scale used by the application.
- density normalization: both sides compared at 960×540, preserving the shared 16:9 aspect ratio.
- state: implementation uses the required default state `备用1` plus `Z-1#巷`; the target image shows populated video, warning and offline examples. Layout and density are comparable, while content-state differences are intentional mock-stage constraints.

## Evidence

- full-view comparison: `artifacts/screenshots/qa-pass2-full-comparison.png`
- video-grid focus: `artifacts/screenshots/qa-pass2-focus-video.png`
- monitor-tree focus: `artifacts/screenshots/qa-pass2-focus-tree.png`
- detail/status focus: `artifacts/screenshots/qa-pass2-focus-detail-status.png`
- collapsed detail runtime evidence: `artifacts/screenshots/wpf-detail-collapsed-scale-pass2.png`

## Required Fidelity Surfaces

- Fonts and typography: Microsoft YaHei UI remains consistent. Application title is 18px; page title 16px; navigation, video titles and group headings 14px; device/body text 13px; auxiliary and status text 11–12px. Main readable information is not below 11px. Weight and hierarchy are visibly closer to the target.
- Spacing and layout rhythm: shell proportions remain 56px / 200px / 300px with 8px main-region and tile gaps. Navigation rows are 44px, video headers 36px, tree sections 38px, tree rows 34px, detail panel 132px and status content 34px. The four-video area remains the dominant region.
- Colors and visual tokens: the requested dark industrial palette is preserved. Selected rows use a lower-opacity blue fill with a thin primary accent; cards use 1px borders and 6px radii without glow.
- Image quality and asset fidelity: no raster or text-glyph icons were introduced. Existing Geometry/Path vector icons remain sharp. Real surveillance imagery is intentionally absent because this phase still uses mock video placeholders.
- Copy and content: video headers now render the complete `组名 · 通道号` form. Main and secondary labels, tabs, status values and current selection text remain readable without truncation at the verified viewports.

## Findings

- No actionable P0, P1 or P2 visual mismatch remains within the user-approved UI-only scope.

## Comparison History

- Formal pass 1: equal-size 960×540 full and focused comparisons found no P0/P1/P2 issues. Final result passed.
- Pre-QA runtime check: the lower detail values were slightly clipped. Field spacing was reduced while retaining the 132px panel height, then the implementation was recaptured. The final screenshot and detail/status focus show all values fully visible.

## Follow-up Polish

- [P3] The target contains real mine video imagery plus warning/offline compositions; the current stage intentionally uses mock video placeholders and online mock data.
- [P3] The target tree contains deeper camera/channel expansion. The current tree remains flat at selectable group level because the user explicitly requested removal of that expansion level while keeping group switching unchanged.
- [P3] The target detail panel includes a thumbnail and more device metadata. This pass retains only the already requested mock fields and does not add data or functionality.
- [P3] The target navigation is proportionally wider than the latest explicit 190–205px requirement. The implementation follows the explicit 200px calibration.

## Implementation Checklist

- [x] Global font and density tokens calibrated.
- [x] Top bar, navigation, video grid, tree, detail panel and status bar calibrated.
- [x] Secondary window remains 540px high with one equal-width three-column row.
- [x] Detail collapse verified without compression.
- [x] Main fullscreen and Esc restoration exercised at runtime.

final result: passed
