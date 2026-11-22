# BLE target dialog layout verification

Date: 2026-11-02

Source visual: `%TEMP%/ble-target-selected-layout.png`

Final implementation: `%TEMP%/ble-target-split-layout-final.png`

Initial implementation: `%TEMP%/ble-target-split-layout.png`

## Comparison

The source is 1619 × 972 pixels; the browser capture is 1280 × 720 CSS pixels at density 1. The reference modal was compared proportionally to the existing 860-pixel shared Studio dialog. Both show Battery Level selected under a device and Battery Service. The browser uses explicitly labeled fixture devices, with two Device Information characteristics instead of the reference's three. Source and implementation were opened together in each comparison input. The full dialog captures make separate focused crops unnecessary.

- Typography: existing Studio fonts, 16-pixel headings and compact body text retained. Long UUIDs wrap without truncation. Source text was normalized by modal width rather than copied at raster pixel size.
- Layout: shared title and footer, compact source/manual-address/scan toolbar, 60/40 tree and read-only inspector. Footer selection count and OK/Cancel remain visible. At a 525-pixel dialog width, the inspector stacks below the tree and the content scrolls.
- Tokens: shared charcoal backgrounds, neutral separators and teal selection. No new palette or font dependency.
- Assets: existing Material icons, no raster decoration needed. Device, service and characteristic icons retain distinct meaning.
- Content: complete UUIDs and original device addresses remain visible in the inspector rather than shortened identifiers. The existing discovery status remains truthful about releasing the connection.

## Findings and iteration

Initial comparison found a P2 scoped-style issue: compact tree icons, row height and hierarchy guides did not apply because the selector expected a component root to carry a native scope attribute. Anchoring these rules to the native browser container fixed the issue. The final capture shows smaller icons, consistent row spacing and hierarchy guides. No remaining P0/P1/P2 layout issues were found.

## Behavior verification

- Fixture scan and automatic discovery populate the device-rooted tree.
- Selecting another device immediately clears the previous characteristic summary and disables confirmation.
- OK applies the target; one Undo removes all selected target fields and Redo restores them together.
- Cancel leaves the original target unchanged.
- Narrow dialog resizing keeps actions accessible.
- No browser console errors were reported during the trial.
- 109 BLE tests passed on each of .NET 8 and .NET 10, including cancellation, late discovery and stale selection coverage. These checks used deterministic test devices, not nearby hardware.

## Follow-up polish

None required for this layout. The source uses shortened standard UUIDs; displaying complete UUIDs is intentional so the inspector exposes the actual stored target.

final result: passed
