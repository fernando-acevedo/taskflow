import { createActionGroup, props } from '@ngrx/store';
import { ApiError } from '@shared/data-access/api-error';
import { Submission } from '@shared/data-access/submission';
import { Label, LabelInput } from '../labels-api.service';

export const LabelsPageActions = createActionGroup({
  source: 'Labels Page',
  events: {
    Opened: props<{ projectId: string }>(),
    'Create Submitted': props<{ label: LabelInput } & Required<Submission>>(),
    'Update Submitted': props<{ labelId: string; changes: LabelInput; rowVersion: string } & Required<Submission>>(),
    'Delete Confirmed': props<{ labelId: string }>(),
    /** The edit dialog asks for the server's version after a stale save. */
    'Refresh Requested': props<Required<Submission>>(),
  },
});

export const LabelsApiActions = createActionGroup({
  source: 'Labels API',
  events: {
    'Labels Loaded': props<{ projectId: string; labels: Label[] }>(),
    'Labels Refreshed': props<{ projectId: string; labels: Label[] } & Submission>(),
    'Labels Load Failed': props<{ error: ApiError }>(),
    'Label Saved': props<{ label: Label; message: string } & Submission>(),
    'Label Deleted': props<{ labelId: string }>(),
    'Label Save Failed': props<{ error: ApiError } & Submission>(),
  },
});
