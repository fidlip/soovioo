import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NgForm } from '@angular/forms';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { LinkedAccount, LinkedAccountsApiService } from '@soovioo/shared';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { NotifyService } from '../../notify.service';
import { UnreadCountsService } from '../../unread-counts.service';
import { LinkedAccountsCard } from './linked-accounts-card';

const ACCOUNT: LinkedAccount = {
  userId: 7,
  schoolName: 'SŠ Údolí',
  username: 'novak',
  unread: 2,
};

/** The protected form and actions, as the template uses them. */
type Internals = {
  schoolSlug: string;
  username: string;
  password: string;
  error: () => string | null;
  link: (form: NgForm) => void;
  unlink: (account: LinkedAccount) => void;
};

describe('LinkedAccountsCard', () => {
  const api = { link: vi.fn(), unlink: vi.fn() };
  const refresh = vi.fn();
  const confirm = vi.fn();

  function open() {
    TestBed.configureTestingModule({
      imports: [LinkedAccountsCard],
      providers: [
        { provide: LinkedAccountsApiService, useValue: api },
        {
          provide: UnreadCountsService,
          useValue: { refresh, linkedAccounts: signal([ACCOUNT]) },
        },
        { provide: ConfirmationService, useValue: { confirm } },
        {
          provide: NotifyService,
          useValue: {
            success: vi.fn(),
            actionFailed: vi.fn(),
            serverFieldErrors: () => null,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(LinkedAccountsCard);
    fixture.detectChanges();
    return {
      fixture,
      card: fixture.componentInstance as unknown as Internals,
    };
  }

  beforeEach(() => vi.clearAllMocks());

  it('lists the linked accounts', () => {
    const { fixture } = open();

    expect(fixture.nativeElement.textContent).toContain('SŠ Údolí – novak');
  });

  it('links the account, empties the form and reloads the list', () => {
    api.link.mockReturnValue(of(undefined));
    const { card } = open();
    card.schoolSlug = 'udoli';
    card.username = 'novak';
    card.password = 'tajne';

    const resetForm = vi.fn();

    card.link({ resetForm } as unknown as NgForm);

    expect(api.link).toHaveBeenCalledWith({
      schoolSlug: 'udoli',
      username: 'novak',
      password: 'tajne',
    });
    expect(resetForm).toHaveBeenCalled();
    expect(refresh).toHaveBeenCalled();
  });

  it("shows the server's reason and keeps what was typed", () => {
    api.link.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: 'Neplatné přihlašovací údaje.',
          }),
      ),
    );
    const { card } = open();
    card.username = 'novak';

    card.link({ resetForm: vi.fn() } as unknown as NgForm);

    expect(card.error()).toBe('Neplatné přihlašovací údaje.');
    expect(card.username).toBe('novak');
    expect(refresh).not.toHaveBeenCalled();
  });

  it('unlinks only after confirming', () => {
    api.unlink.mockReturnValue(of(undefined));
    const { card } = open();

    card.unlink(ACCOUNT);

    expect(api.unlink).not.toHaveBeenCalled();
    confirm.mock.calls[0][0].accept();
    expect(api.unlink).toHaveBeenCalledWith(7);
    expect(refresh).toHaveBeenCalled();
  });
});
