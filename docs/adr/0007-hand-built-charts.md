# 7. Draw the dashboard charts with HTML and CSS instead of a chart library

- Status: Accepted
- Date: 2026-10-19

## Context

The analysis planned ng2-charts (Chart.js) for the dashboard. The dashboard ended up needing three kinds of
figure: stat tiles, one stacked bar for tasks by status (part-to-whole, four fixed segments), and horizontal bars
for open tasks by assignee and by priority (magnitude). None needs axes, time scales, zooming or animation.

## Decision

- Build the three as small presentational components in `features/dashboard/ui/` (`StatTile`, `StatusBar`,
  `BarList`) with plain HTML and CSS, and `matTooltip` for hover details.
- Status colors are the first four slots of a categorical palette, in a fixed order, validated for color-vision
  deficiency separation on the Material surface. They are CSS variables (`--tf-chart-*`) in `styles.scss`.
  Magnitude bars use one hue. Values and labels use text tokens, never the series color.
- Every chart shows its numbers as text (legend with counts and shares, a value per bar), so it doubles as its
  table view and nothing depends on color alone.

## Consequences

- No chart library in the bundle, and the charts follow the Material theme, the language
  switch and the page's accessibility tree like any other markup.
- Charts are plain components: unit tests read their DOM directly.
- Line charts, time series or interactive zoom would need a library; adopt one when such a chart is planned.
