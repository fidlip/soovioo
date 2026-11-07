import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { LinkableSchool, LinkedAccountsApiService } from '@soovioo/shared';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { NotifyService } from '../notify.service';
import { LinkAccountDialog } from './link-account-dialog';

/** The protected state and actions, as the template uses them. */
type Internals = {
  school: LinkableSchool | string | null;
  suggestions: () => LinkableSchool[];
  error: () => string | null;
  chosen: () => LinkableSchool | null;
  search: (event: { query: string }) => void;
  proceed: () => void;
};

const SCHOOL = { name: 'SŠ Údolí', schoolSlug: 'udoli' };

describe('LinkAccountDialog', () => {
  const api = { searchSchools: vi.fn(), startRequest: vi.fn() };

  function open() {
    TestBed.configureTestingModule({
      imports: [LinkAccountDialog],
      providers: [
        { provide: LinkedAccountsApiService, useValue: api },
        {
          provide: NotifyService,
          useValue: { serverFieldErrors: () => null },
        },
      ],
    });
    return TestBed.createComponent(LinkAccountDialog)
      .componentInstance as unknown as Internals;
  }

  beforeEach(() => vi.clearAllMocks());

  it('suggests the schools the server finds', () => {
    api.searchSchools.mockReturnValue(of([SCHOOL]));
    const dialog = open();

    dialog.search({ query: 'udo' });

    expect(api.searchSchools).toHaveBeenCalledWith('udo');
    expect(dialog.suggestions()).toEqual([SCHOOL]);
  });

  it('text typed but not picked from the list starts nothing', () => {
    const dialog = open();
    dialog.school = 'udoli';

    dialog.proceed();

    expect(dialog.chosen()).toBeNull();
    expect(api.startRequest).not.toHaveBeenCalled();
  });

  it("shows the server's reason", () => {
    api.startRequest.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, error: 'Nejde to.' }),
      ),
    );
    const dialog = open();
    dialog.school = SCHOOL;

    dialog.proceed();

    expect(api.startRequest).toHaveBeenCalledWith('udoli');
    expect(dialog.error()).toBe('Nejde to.');
  });
});
