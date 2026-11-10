/** Public API of the labels feature: what other features may import. */
export { labelsFeature } from './data-access/store/labels.feature';
export { LabelsApiActions, LabelsPageActions } from './data-access/store/labels.actions';
export type { LabelSummary } from './data-access/labels-api.service';
export { LabelChip } from './ui/label-chip';
export { LabelsManager } from './ui/labels-manager';
export * as labelsEffects from './data-access/store/labels.effects';
