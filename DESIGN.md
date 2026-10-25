---
name: FusionNetPro
description: Native Windows workspace for measured connection data and reviewed, reversible gaming sessions.
colors:
  accent: "#3B82F6"
  accent-secondary: "#60A5FA"
  accent-ink: "#04121A"
  bg-deep: "#0F0F12"
  bg: "#09090B"
  surface: "#111113"
  surface-alt: "#18181B"
  surface-hover: "#27272A"
  border: "#27272A"
  border-strong: "#3F3F46"
  control-border: "#71717A"
  text: "#FAFAFA"
  text-muted: "#A1A1AA"
  text-dim: "#8E8E96"
  success: "#34D399"
  warning: "#FBBF24"
  danger: "#F87171"
  info: "#60A5FA"
typography:
  page-title:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "24px"
    fontWeight: 600
  section-title:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "16px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "14px"
    fontWeight: 400
  caption:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "13px"
    fontWeight: 400
  metric:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "32px"
    fontWeight: 600
  metric-small:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "22px"
    fontWeight: 600
  mono:
    fontFamily: "Cascadia Mono, Consolas, Courier New"
    fontSize: "12px"
    fontWeight: 400
rounded:
  tab: "8px 8px 0 0"
  flat: "8px"
  input: "8px"
  button: "8px"
  card: "10px"
  hero: "12px"
  pill: "12px (chips 15px; WPF clamps 999 into an ellipse)"
spacing:
  compact: "8px"
  group: "16px"
  panel: "24px"
  page-leading: "32px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
    rounded: "{rounded.button}"
    padding: "10px 18px"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.text}"
    rounded: "{rounded.button}"
    padding: "9px 16px"
  input-text:
    backgroundColor: "{colors.surface-alt}"
    textColor: "{colors.text}"
    rounded: "{rounded.input}"
    padding: "8px 11px"
    typography: "{typography.caption}"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.card}"
    padding: "{spacing.panel}"
  navigation:
    backgroundColor: "transparent"
    textColor: "{colors.text-muted}"
    rounded: "{rounded.button}"
    padding: "9px 12px"
  target-chip:
    backgroundColor: "transparent"
    textColor: "{colors.text-muted}"
    rounded: "{rounded.pill}"
    padding: "6px 13px"
  workspace-tab:
    backgroundColor: "transparent"
    textColor: "{colors.text-muted}"
    rounded: "{rounded.tab}"
    padding: "12px 16px"
---

# Design System: FusionNetPro

## Overview

**Creative North Star: "Measured Wayfinding"**

The visual world is a quiet native Windows workspace: near-black zinc ground, zinc work surfaces with hairline borders, one blue action accent and explicit semantic status. On 2026-10-25 the owner rejected the previous slate look; the replacement reference is awesome-design-skills/shadcn in its dark (zinc) form, adapted to WPF and Arabic text without web fonts or a web framework. The Dashboard leads with the automatic Fusion Mode state (state, detected game, engage, auto toggle, admin notice), then connection measurements in hairline-separated columns; manual Game Boost review is the secondary row.

Named data, source labels and current state carry the hierarchy. This code-led replacement is documented as implemented, not as final user visual approval. The FusionNetPro name and existing logo remain. Low ambient light during play is a design assumption.

**Key Characteristics:**

- Charcoal and slate tonal layers with blue action emphasis.
- Native Segoe text and explicit LTR technical values inside an RTL-capable shell.
- Wrapping groups, vertical scrolling and progressive detail.
- Words accompany status; missing measurements remain unavailable.

Resource owners are `src/FusionNet.App/Themes/Colors.xaml` (palette/families), `src/FusionNet.App/Themes/Styles.xaml` (text, cards, buttons, focus, fields, navigation and tabs) and `src/FusionNet.App/Themes/Controls.xaml` (toggle, progress and slider). Tokens use portable CSS px for WPF device-independent units at 96 DPI. Opaque WPF `#FFRRGGBB` is represented as CSS `#RRGGBB`; the source resources remain authoritative.

The direction contract remains in `docs/UI_REDESIGN_BRIEF.md` and `.impeccable/surfaces/src-fusionnet-app-mainwindow-xaml.md`. The connection-first overview, scan → review → start sequence and separate advanced settings belong to that surface strategy, not a mandatory composition for every screen.

## Colors

Cool slate neutrals carry the work; action blue is distinct from semantic status.

### Primary

- **Action Blue** (`accent`): primary actions, selected navigation, focus, selected target and chart stroke.
- **Companion Blue** (`accent-secondary`): the second endpoint of existing accent gradients.
- **Deep Action Ink** (`accent-ink`): primary button text, checked checkbox mark and active toggle thumb.

### Neutral

- **Charcoal Ground** (`bg-deep`, `bg`): sidebar and window.
- **Slate Work Surface** (`surface`, `surface-alt`, `surface-hover`): grouped work, fields and hover.
- **Structural Lines** (`border`, `border-strong`): dividers and outlined actions. **Control Outline** (`control-border`) is deliberately brighter for enabled fields, combo toggles and unchecked checkboxes.
- **Reading Ink** (`text`, `text-muted`, `text-dim`): content, supporting text and captions. Dim remains readable content.

### Semantic status

- **Measured Success**, **Review Amber**, **Failure Rose** and **Information Blue** (`success`, `warning`, `danger`, `info`) retain semantic roles and accompanying text.
- Accent soft/faint brushes use alpha bytes `26` / `14`; accent borders use `66`. Success/warning/danger soft and border brushes use `26` / `66`. These are composited brushes rather than new opaque backgrounds.

**The Resource Ownership Rule.** Read colors and control styles through DynamicResource and the existing named styles. ThemeService owns accent replacement; a new screen must not hard-code a competing accent.

Customization uses the five preset IDs in `src/FusionNet.App/Services/ToastService.cs` (`ThemeService`): `cyan` is the default blue; `violet`, `emerald`, `amber` and `rose` replace accent endpoints, derived alpha brushes and the spark gradient. Action ink stays fixed. The ID `cyan` is a compatibility name, not an instruction to restore the old cyan value. Status and neutral brushes stay independent. Every preset/state/gradient combination has not been visually certified.

## Typography

**Display and body:** `Font.Display` and `Font.UI` both use Segoe UI Variable Text with Segoe UI fallback. The brief's “Segoe UI Arabic” describes the language need; the actual family is this native Segoe stack. No custom Arabic font is bundled.

**Technical text:** `Font.Mono` uses Cascadia Mono, Consolas and Courier New. The UI does not set a global tabular numeral feature; technical alignment uses explicit LTR containers, mono details and measured group widths.

### Hierarchy

- **Page title:** `Text.PageTitle`, semibold, for page identity.
- **Section title:** `Text.SectionTitle`, semibold, for a work group.
- **Body:** `Text.Body`, wrapping, for content and explanation.
- **Supporting/caption:** `Text.Muted` / `Text.Caption`; muted wraps, captions need explicit wrapping for growing content.
- **Metric / small metric:** `Text.Metric` / `Text.MetricSmall`; adjacent units and availability.
- **Mono:** `Text.Mono`, for addresses, paths, timestamps and resource details.

Sizes and weights are in the frontmatter. WPF native line layout is used; no global fixed line-height or tracking token exists. `Text.Brand` is a local shell treatment (15 DIP bold), not another page heading.

**The Evidence Beside Data Rule.** Keep units, source and availability with a measurement. Interface throughput and connection counts must not be presented as measured per-process transfer or game-server latency.

## Layout

`src/FusionNet.App/MainWindow.xaml` starts at 1500 × 960 DIP with a 1080 × 680 DIP minimum. Native custom chrome is 54 DIP high; the fixed sidebar is 240 DIP, and the remaining column stretches. Page leading/top/trailing margins are 32/24/24 DIP. RTL mirrors logical shell placement; the English shell uses LTR.

There are no CSS media queries, mobile drawer or explicit width breakpoint switches. `Views/Dashboard/DashboardView.xaml`, `Views/Security/LocalMonitorPanel.xaml` and `Views/Tools/ToolsView.xaml` (under `src/FusionNet.App/`) use wrapping groups and vertical scrolling with horizontal scrolling disabled. Groups reflow according to measured width, not screenshot scaling. A 790-DIP fixture is an isolated page host, not a supported 790-DIP shell.

The reused rhythm is 8/16/24/32 DIP with local exceptions. Dashboard metric groups are 138 DIP plus 16-DIP gaps; adapter rates use 166 DIP; the chart is 128 DIP high. Monitor summary groups use 156 DIP, search is 260 DIP, and the virtualized recycling process list has a 420-DIP viewport. Tools DNS groups use 250/190/110 DIP, and sensor rows use 244 DIP with wrapping. Long paths/results wrap and process identity details expand. Preserve list virtualization and progressive disclosure.

## Elevation & Depth

Ordinary shell, cards and process rows use flat tonal layering and thin borders. The replacement workspace does not depend on floating shadows or backdrop blur. Separators establish groups. Existing tooltips request native `HasDropShadow=True`; this is a local exception, not a card shadow token.

**The Quiet Surface Rule.** Use tonal surfaces and thin dividers for ordinary workspace structure. A card groups a real work unit; do not add a card around every label or metric.

Motion remains limited: the existing toggle thumb translates 20 DIP over 160 ms with cubic ease-out in `Themes/Controls.xaml`. Main button states change opacity without a new transition. The current progress template has no explicit indeterminate animation; a visible busy bar does not prove animated progress.

## Shapes

Use the frontmatter radius roles from the actual templates: modest field/button rounding, larger work containers and round chips. Borders are normally 1 DIP. Selected workspace tabs have a 2-DIP lower rule; selected navigation has a 3 × 18 DIP accent indicator. Rounded forms are useful controls/containers rather than decoration around every data point.

## Components

### Buttons

`Btn.Base` provides 40-DIP minimum height, semibold 13-DIP text and the common template. `Btn.Primary` uses Action Blue, Deep Action Ink and bold text. `Btn.Ghost` is transparent with a strong outline. Existing `Btn.Link` supports subordinate actions; existing danger/success gradients remain local semantic variants.

The main button root uses opacity 0.88 on hover, 0.72 pressed and 0.38 disabled. Preserve shared state handling when extending styles.

### Focus and contrast

`FocusRing` in `Themes/Styles.xaml` is a 2-DIP accent border, 8-DIP radius and -3-DIP margin. It is explicitly attached to main buttons, navigation, workspace tabs, text fields, combo boxes, checkboxes and the custom toggle. Text boxes additionally change border on `IsKeyboardFocusWithin`. Do not infer identical explicit focus treatment for every legacy icon button, chip, slider or expander.

Enabled outline contrast and new shell/dashboard localization were corrected and independently reported resolved. That scoped disposition does not certify the whole UI. Verify readable text, enabled field/checkbox outlines, selected states and visible keyboard focus in the chosen native preset. Actual keyboard traversal and live runtime were not exercised in this documentation pass.

### Inputs / Fields

`Input.TextBox` uses alternate slate, Control Outline, accent caret/selection and input corners; disabled root opacity is 0.45. `Input.ComboBox` is 36 DIP high with the same enabled outline and a bordered slate popup. `Input.CheckBox` is a 19-DIP square with 6-DIP corners; checked state uses accent and deep ink. Preserve persistent labels and automation names. Technical paths, hosts and addresses remain LTR.

### Navigation, chips and tabs

`Btn.Nav` is muted at rest; hover uses slate-hover and reading ink; selected uses accent-faint, accent text and an indicator. Shell labels override the base style to 14 DIP. Selection has shape as well as color.

`Btn.Chip` is a target selector with accent-soft background and accent border when checked; it is not a decorative status badge. `Workspace.Tab` uses muted unselected text and selected reading ink, accent-faint background and lower accent rule. `Workspace.Tabs` keeps the native tab panel above selected content.

### Cards / Containers and rows

`Card` groups a work unit with slate surface, structural border and panel padding. `Card.Flat` uses alternate surface and 14-DIP padding. `Card.Hero` remains in the dictionary without becoming a promotional requirement. Monitor processes, tool results and activities use divider-led rows, concise identity, readable resources and expandable evidence.

### States and provenance

`Views/Security/ReportReviewWindow.cs` places the exact consent/report title in a wrapping `Text.Body` heading inside the window, above the unchanged read-only LTR payload and explicit approve/cancel buttons. Window defaults are 780 × 560 DIP, minimum400 × 300. The notice must remain readable even when the native title bar clips long text. Version3.2.2 synthetic content renders at400/780-DIP widths cover English and400-DIP Arabic (`qa/screenshots/report-review-*-offline.png`); no dialog was shown or approved. Actual keyboard/DPI behavior remains unverified.

Missing samples, empty activity, busy/cancel and error text are content with explicit limits. This pass inspected synthetic `qa/screenshots/shell-1080-offline.png`, `shell-1500-offline.png`, `gameboost-narrow-offline.png` and `tools-790-offline.png` against the source. Additional English, bottom, security and state captures exist; existence does not establish full state coverage here. No new shipping raster was introduced; the existing logo remains.

Sidecar HTML/CSS snippets are design-panel visual translations of WPF primitives, not executable WPF templates or a second shipping implementation. Synthetic tonal ramps are preview metadata, not additional app presets.

## Do's and Don'ts

### Do:

- **Do** reuse the named WPF brushes and styles, preserving DynamicResource accent updates.
- **Do** keep Arabic prose RTL and paths, addresses, timestamps and technical measurements explicitly LTR where the source does.
- **Do** wrap action groups and long evidence, retaining source and unavailable-state text.
- **Do** keep status words, consent review, stop and restore actions readable at compact sizes.
- **Do** verify native renders and actual keyboard behavior before claiming accessibility completion.

### Don't:

- **Don't** import the Sleek sample web palette, Inter fonts or browser layout rules over native resources.
- **Don't** turn unknown readings into zeroes, connection counts into transfer rates, or synthetic fixtures into product claims.
- **Don't** make the accent preset alter success, warning or danger semantics.
- **Don't** promote legacy glyph icons, eyebrow styles or unreviewed interaction states into new decorative patterns.
- **Don't** claim final user approval, full English localization or live OS integration validation from offline captures.

Remaining drift is not canonized: monitor/tools interface labels and consent copy now use bilingual resources; raw evidence, approved snapshots and AI output retain their original language; legacy glyph icons and eyebrow styles do not authorize new ornament. Native Segoe use is a platform adaptation. Final user visual acceptance, live keyboard behavior, DPI/high-contrast modes, all preset/state combinations and scan/Firewall/task/ETW/API/installer/in-game behavior are not established by this document. Release/package verification is separate from design evidence.

