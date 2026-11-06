import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthApiService } from '@soovioo/shared';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { SchoolContextService } from '../school-context';
import { AccountSwitchPage } from './account-switch-page';

describe('AccountSwitchPage', () => {
  const redeemLoginCode = vi.fn<(code: string) => Observable<unknown>>();

  function open(hash: string) {
    history.replaceState(null, '', `/udoli/account/switch${hash}`);
    TestBed.configureTestingModule({
      imports: [AccountSwitchPage],
      providers: [
        provideRouter([]),
        { provide: AuthApiService, useValue: { redeemLoginCode } },
        {
          provide: SchoolContextService,
          useValue: { link: (page: string) => ['/udoli', page] },
        },
      ],
    });
    const navigate = vi
      .spyOn(TestBed.inject(Router), 'navigate')
      .mockResolvedValue(true);
    const fixture = TestBed.createComponent(AccountSwitchPage);
    fixture.detectChanges();
    return { fixture, navigate };
  }

  beforeEach(() => redeemLoginCode.mockReset());

  it('redeems the code from the fragment, drops it from the address and goes Home', () => {
    redeemLoginCode.mockReturnValue(of({}));

    const { navigate } = open('#code=abc');

    expect(redeemLoginCode).toHaveBeenCalledWith('abc');
    expect(window.location.hash).toBe('');
    expect(navigate).toHaveBeenCalledWith(['/udoli', 'home']);
  });

  it('offers logging in when the code is refused', () => {
    redeemLoginCode.mockReturnValue(throwError(() => new Error('400')));

    const { fixture, navigate } = open('#code=abc');
    fixture.detectChanges();

    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Přihlaste se prosím');
    expect(fixture.nativeElement.textContent).toContain('Přihlásit se');
  });

  it('asks the server nothing without a code', () => {
    const { fixture } = open('');
    fixture.detectChanges();

    expect(redeemLoginCode).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Chybí kód');
  });
});
