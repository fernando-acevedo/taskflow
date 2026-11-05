import { createActionGroup, props } from '@ngrx/store';
import { ApiError } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { Label, LabelInput } from '../labels-api.service';

export const LabelsPageActions = createActionGroup({
  source: 'Labels Page',
  events: {
    Opened: props<{ projectId: string }>(),
    'Create Submitted': props<{ label: LabelInput } & Required<Submission>>(),
    'Update Submitted': props<{ labelId: string; changes: LabelInput } & Required<Submission>>(),
    'Delete Confirmed': props<{ labelId: string }>(),
  },
});

export const LabelsApiActions = createActionGroup({
  source: 'Labels API',
  events: {
    'Labels Loaded': props<{ projectId: string; labels: Label[] }>(),
    'Labels Load Failed': props<{ error: ApiError }>(),
    'Label Saved': props<{ label: Label; message: string } & Submission>(),
    'Label Deleted': props<{ labelId: string }>(),
    'Label Save Failed': props<{ error: ApiError } & Submission>(),
  },
});
