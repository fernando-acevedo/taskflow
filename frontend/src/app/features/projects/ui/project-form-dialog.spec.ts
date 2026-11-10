import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { Project } from '../data-access/projects-api.service';
import { ProjectFormData, ProjectFormDialog } from './project-form-dialog';

const stale = { code: 'Concurrency.Stale', fields: {} };

function aProject(overrides: Partial<Project> = {}): Project {
  return { id: 'p1', clientId: 'c1', clientName: 'Acme', name: 'Website', description: null, rowVersion: 'v1', ...overrides } as Project;
}

describe('ProjectFormDialog when someone else changed the project (stale)', () => {
  function render(data: Partial<ProjectFormData>) {
    TestBed.configureTestingModule({
      imports: [
        ProjectFormDialog,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { project: aProject(), clients: [{ id: 'c1', name: 'Acme' }], save: vi.fn(), ...data } },
      ],
    });
    const fixture = TestBed.createComponent(ProjectFormDialog);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ReturnType<typeof render>) => fixture.nativeElement as HTMLElement;

  function typeName(fixture: ReturnType<typeof render>, value: string): void {
    const input = root(fixture).querySelector<HTMLInputElement>('[formControlName="name"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function submit(fixture: ReturnType<typeof render>): void {
    root(fixture).querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function press(fixture: ReturnType<typeof render>, index: number): void {
    root(fixture).querySelectorAll<HTMLButtonElement>('app-stale-notice button')[index].click();
    fixture.detectChanges();
  }

  it('stays open with an alert and keeps what was typed', () => {
    const fixture = render({ save: vi.fn(() => of(stale)) });
    typeName(fixture, 'Mine');
    submit(fixture);

    expect(root(fixture).querySelector('[role="alert"]')).not.toBeNull();
    expect(root(fixture).querySelector<HTMLInputElement>('[formControlName="name"]')?.value).toBe('Mine');
  });

  it('Reload replaces the form with the server version', () => {
    const refresh = vi.fn(() => of(aProject({ name: 'Theirs' })));
    const fixture = render({ save: vi.fn(() => of(stale)), refresh });
    typeName(fixture, 'Mine');
    submit(fixture);

    press(fixture, 0);

    expect(root(fixture).querySelector<HTMLInputElement>('[formControlName="name"]')?.value).toBe('Theirs');
    expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
  });

  it('Save anyway fetches the server version and saves the typed values again', () => {
    const save = vi.fn().mockReturnValueOnce(of(stale)).mockReturnValueOnce(of(null));
    const refresh = vi.fn(() => of(aProject({ name: 'Theirs' })));
    const fixture = render({ save, refresh });
    typeName(fixture, 'Mine');
    submit(fixture);

    press(fixture, 1);

    expect(save).toHaveBeenCalledTimes(2);
    expect(save).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'Mine' }));
  });
});
