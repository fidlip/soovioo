import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { AuthApiService, LinkedAccountsApiService } from '@soovioo/shared';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { NotifyService } from '../notify.service';
import { SchoolContextService } from '../school-context';
import { AccountLinkDonePage } from './account-link-done-page';
import { AccountLinkPage } from './account-link-page';

const PREVIEW = {
  username: 'rodic',
  schoolName: 'ZŠ Kopec',
  schoolSlug: 'kopec',
};

describe('linking accounts', () => {
  const api = {
    previewRequest: vi.fn(),
    approveRequest: vi.fn(),
    completeRequest: vi.fn(),
  };
  const auth = {
    isLoggedIn: vi.fn(),
    getUsername: () => 'novak',
    getSchoolName: () => 'SŠ Údolí',
    logout: vi.fn(),
  };
  const notify = { success: vi.fn() };

  function configure(fragment: string | null) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LinkedAccountsApiService, useValue: api },
        { provide: AuthApiService, useValue: auth },
        { provide: NotifyService, useValue: notify },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { fragment } },
        },
        {
          provide: SchoolContextService,
          useValue: { link: (page: string) => ['/udoli', page] },
        },
      ],
    });
    return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  }

  beforeEach(() => vi.clearAllMocks());

  describe('at the other school', () => {
    it('sends whoever is not logged in to its login, with the code', () => {
      auth.isLoggedIn.mockReturnValue(false);
      const navigate = configure('code=abc');

      TestBed.createComponent(AccountLinkPage);

      expect(navigate).toHaveBeenCalledWith(['/udoli', 'login'], {
        queryParams: { link: 1 },
        fragment: 'code=abc',
      });
      expect(api.previewRequest).not.toHaveBeenCalled();
    });

    it('names both accounts and approves nothing before the click', () => {
      auth.isLoggedIn.mockReturnValue(true);
      api.previewRequest.mockReturnValue(of(PREVIEW));
      configure('code=abc');

      const fixture = TestBed.createComponent(AccountLinkPage);
      fixture.detectChanges();

      const text = fixture.nativeElement.textContent;
      expect(text).toContain('novak');
      expect(text).toContain('SŠ Údolí');
      expect(text).toContain('rodic');
      expect(text).toContain('ZŠ Kopec');
      expect(api.approveRequest).not.toHaveBeenCalled();
    });

    it('another account means logging out and logging in again', () => {
      auth.isLoggedIn.mockReturnValue(true);
      api.previewRequest.mockReturnValue(of(PREVIEW));
      const navigate = configure('code=abc');
      const fixture = TestBed.createComponent(AccountLinkPage);
      fixture.detectChanges();

      (
        [...fixture.nativeElement.querySelectorAll('button')].find(
          (b: HTMLButtonElement) => b.textContent?.includes('jiný účet'),
        ) as HTMLButtonElement
      ).click();

      expect(auth.logout).toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(
        ['/udoli', 'login'],
        expect.objectContaining({ fragment: 'code=abc' }),
      );
    });

    it("says the server's reason for a request that ran out", () => {
      auth.isLoggedIn.mockReturnValue(true);
      api.previewRequest.mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 400, error: 'Vypršela.' }),
        ),
      );
      configure('code=abc');

      const fixture = TestBed.createComponent(AccountLinkPage);
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Vypršela.');
    });

    it('asks nothing without a code', () => {
      auth.isLoggedIn.mockReturnValue(true);
      configure(null);

      const fixture = TestBed.createComponent(AccountLinkPage);
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Chybí kód');
      expect(api.previewRequest).not.toHaveBeenCalled();
    });
  });

  describe('back where it started', () => {
    it('finishes, says so and goes to Nastavení', () => {
      auth.isLoggedIn.mockReturnValue(true);
      api.completeRequest.mockReturnValue(of(undefined));
      const navigate = configure('code=zpet');

      TestBed.createComponent(AccountLinkDonePage);

      expect(api.completeRequest).toHaveBeenCalledWith('zpet');
      expect(notify.success).toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/udoli', 'settings']);
    });

    it("shows the server's reason and links nothing", () => {
      auth.isLoggedIn.mockReturnValue(true);
      api.completeRequest.mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 400, error: 'Neplatná.' }),
        ),
      );
      configure('code=zpet');

      const fixture = TestBed.createComponent(AccountLinkDonePage);
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Neplatná.');
      expect(notify.success).not.toHaveBeenCalled();
    });

    it('someone not logged in here is told and asks nothing', () => {
      auth.isLoggedIn.mockReturnValue(false);
      configure('code=zpet');

      const fixture = TestBed.createComponent(AccountLinkDonePage);
      fixture.detectChanges();

      expect(api.completeRequest).not.toHaveBeenCalled();
      expect(fixture.nativeElement.textContent).toContain('vypršelo');
    });
  });
});
