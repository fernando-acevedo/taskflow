import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { of } from 'rxjs';
import { Notifier } from '@core/notifications/notifier';
import { Submissions } from '@shared/data-access/submission';
import { LabelsPageActions } from '../data-access/store/labels.actions';
import { labelsFeature } from '../data-access/store/labels.feature';
import { LabelFormDialog } from './label-form-dialog';
import { LabelsManager } from './labels-manager';

describe('LabelsManager', () => {
  const dialog = { open: vi.fn() };
  const notifier = { info: vi.fn() };
  const dispatchAndWait = vi.fn();
  let store: MockStore;

  const labels = [
    { id: 'l1', name: 'Bug', color: '#D32F2F' },
    { id: 'l2', name: 'Idea', color: '#388E3C' },
  ];

  function render(items: typeof labels): HTMLElement {
    dialog.open.mockReset();
    notifier.info.mockReset();
    dispatchAndWait.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        LabelsManager,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: labelsFeature.selectAll, value: items },
            { selector: labelsFeature.selectSaving, value: false },
          ],
        }),
        { provide: MatDialog, useValue: dialog },
        { provide: Submissions, useValue: { dispatchAndWait } },
        { provide: Notifier, useValue: notifier },
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');
    const fixture = TestBed.createComponent(LabelsManager);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the labels', () => {
    expect(render(labels).querySelectorAll('li')).toHaveLength(2);
  });

  it('says so when there are none', () => {
    expect(render([]).querySelector('.empty')).not.toBeNull();
  });

  it('opens the form dialog to create and to edit', () => {
    const element = render(labels);

    element.querySelector<HTMLButtonElement>('mat-card-actions button')!.click();
    element.querySelector<HTMLButtonElement>('li button')!.click();

    expect(dialog.open).toHaveBeenCalledTimes(2);
    expect(dialog.open.mock.calls[0]).toEqual([LabelFormDialog, expect.objectContaining({ data: expect.objectContaining({ label: null }) as unknown })]);
    expect(dialog.open.mock.calls[1]).toEqual([
      LabelFormDialog,
      expect.objectContaining({ data: expect.objectContaining({ label: labels[0] }) as unknown }),
    ]);
  });

  it('deletes only after the confirmation', () => {
    const element = render(labels);
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    element.querySelectorAll('li')[1].querySelectorAll('button')[1].click();

    expect(store.dispatch).toHaveBeenCalledWith(LabelsPageActions.deleteConfirmed({ labelId: 'l2' }));
  });

  it('does not delete when the confirmation is dismissed', () => {
    const element = render(labels);
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    element.querySelectorAll('li')[1].querySelectorAll('button')[1].click();

    expect(store.dispatch).not.toHaveBeenCalled();
  });
  describe('row version of an edit', () => {
    const rows = [{ id: 'l1', name: 'Bug', color: '#D32F2F', rowVersion: 'rv1' }];
    const input = { name: 'Bug 2', color: '#D32F2F' };

    function openEdit() {
      const element = render(rows);
      const ref = { close: vi.fn() };
      dialog.open.mockReturnValue(ref);
      element.querySelector<HTMLButtonElement>('li button')!.click();
      const data = (dialog.open.mock.calls[0][1] as { data: { save: (i: unknown) => unknown; refresh: () => { subscribe: (f: (l: unknown) => void) => void } } }).data;
      return { data, ref };
    }

    it('sends the version the dialog opened with even if the store was reloaded meanwhile', () => {
      const { data } = openEdit();
      store.overrideSelector(labelsFeature.selectAll, [{ ...rows[0], rowVersion: 'rv-theirs' }] as never);
      store.refreshState();

      data.save(input);

      expect(dispatchAndWait).toHaveBeenCalledWith(expect.objectContaining({ rowVersion: 'rv1', changes: input }));
    });

    it('takes the reloaded version only after a successful refresh (Reload or Save anyway)', () => {
      const { data } = openEdit();
      dispatchAndWait.mockReturnValue(of(null));
      store.overrideSelector(labelsFeature.selectAll, [{ ...rows[0], rowVersion: 'rv-theirs' }] as never);
      store.refreshState();

      data.refresh().subscribe(() => undefined);
      data.save(input);

      expect(dispatchAndWait).toHaveBeenLastCalledWith(expect.objectContaining({ rowVersion: 'rv-theirs' }));
    });

    it('says it no longer exists and closes the dialog when the label is gone after reloading', () => {
      const { data, ref } = openEdit();
      dispatchAndWait.mockReturnValue(of(null));
      store.overrideSelector(labelsFeature.selectAll, [] as never);
      store.refreshState();
      const result = vi.fn();

      data.refresh().subscribe(result);

      expect(result).toHaveBeenCalledWith(null);
      expect(notifier.info).toHaveBeenCalledWith('common.gone');
      expect(ref.close).toHaveBeenCalled();
    });
  });
});
