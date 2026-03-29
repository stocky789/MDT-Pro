# CAD / MDC UI tokens (WPF)

Source: `native/src/MDTProNative.Wpf/Themes/CadResources.xaml`. The theme is a **generic** dark, dispatch-style look for an in-vehicle data terminal; it is **not** meant to imitate any specific product or vendor UI.

## Color roles

| Role | Resource | Hex | Use |
|------|-----------|-----|-----|
| Background | `CadBg` | `#0A0C10` | Window / page backdrop |
| Panel | `CadPanel` | `#12151C` | List surfaces, secondary chrome |
| Elevated | `CadElevated` | `#1A1F2A` | Inputs and buttons (slightly lifted from panel) |
| Border | `CadBorder` | `#2D3548` | Dividers, control outlines |
| Text | `CadText` | `#E8ECF3` | Primary foreground |
| Muted | `CadMuted` | `#8B95A8` | Section labels, secondary copy |
| Accent | `CadAccent` | `#4A9EFF` | Focus / emphasis (caret, accent button border) |
| Urgent | `CadUrgent` | `#FF4D4D` | High-attention states (e.g. offline / alarm tone) |
| Status bar | `CadStatusBar` | `#07080C` | Top connection strip (darker than main bg) |

`CadAccentButton` uses a fixed accent-tinted fill `#1E3A5F` with `CadAccent` as the border.

## Typography

- **Chrome (default):** `Window` style sets **Segoe UI**, size **13** — labels, buttons, titles inherit unless overridden.
- **CAD readouts / log:** **Consolas**, typically **12** on list and detail controls in `MainWindow.xaml` (time, location, unit, callout list, detail body, message log). Monospace keeps columnar dispatch text aligned and scannable.

## Panel layout goals (generic in-car MDC pattern)

1. **Persistent top strips** — A narrow **status / connection** bar (`CadStatusBar`) and an **operational status** row (`CadPanel`) mirror the always-visible “vehicle + time + unit” chrome common on mobile data terminals.
2. **Primary work area** — Fixed-width **event / callout list** beside a **flexible detail** pane: quick triage on the left, full narrative or structured detail on the right.
3. **Bottom message log** — A dedicated **CAD traffic / system log** band (shorter row ratio) for chronological lines without crowding the main list.
4. **Visual hierarchy** — Muted uppercase section headers, bordered panels, and consistent padding so glare and arm’s-length reading stay tolerable in a vehicle context.

## Using tokens in XAML

Reference brushes as `{StaticResource CadBg}`, `{StaticResource CadText}`, etc. Control styles: `CadTextBox`, `CadButton`, `CadAccentButton`, `CadLabelMuted`, `CadListBox`.
