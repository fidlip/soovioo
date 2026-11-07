import { MessageService } from '@openng/optimus-ui/api';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, FeedbackRequest, TokenStorage } from '@soovioo/shared';
import { Observable, of } from 'rxjs';
import { FeedbackForm } from './feedback-form';

class FakeTokenStorage extends TokenStorage {
  getToken() {
    return null;
  }
  setToken() {
    /* unused */
  }
  clearToken() {
    /* unused */
  }
}

describe('FeedbackForm', () => {
  type Form = {
    rating: { set(value: number | null): void };
    text: string;
    contact: string | null;
    consent: boolean;
    contactValue(): string;
    canSend(): boolean;
    submit(): void;
  };

  const setup = (email: string) => {
    TestBed.configureTestingModule({
      imports: [FeedbackForm],
      providers: [
        MessageService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: new FakeTokenStorage() },
      ],
    });
    const sent: FeedbackRequest[] = [];
    const fixture = TestBed.createComponent(FeedbackForm);
    fixture.componentRef.setInput(
      'send',
      (request: FeedbackRequest): Observable<void> => {
        sent.push(request);
        return of(undefined);
      },
    );
    fixture.componentRef.setInput('email', email);
    fixture.detectChanges();
    return { form: fixture.componentInstance as unknown as Form, sent };
  };

  it('offers the account e-mail and sends the consent only with one', () => {
    const { form, sent } = setup('jan@example.com');

    expect(form.contactValue()).toBe('jan@example.com');
    expect(form.canSend()).toBe(false);
    form.rating.set(4);
    form.text = '  Chybí tisk.  ';
    form.consent = true;
    form.submit();

    form.text = 'Ještě něco';
    form.contact = ' ';
    form.consent = true;
    form.submit();

    expect(sent).toEqual([
      expect.objectContaining({
        rating: 4,
        text: 'Chybí tisk.',
        email: 'jan@example.com',
        consent: true,
      }),
      expect.objectContaining({
        rating: null,
        text: 'Ještě něco',
        email: null,
        consent: false,
      }),
    ]);
  });

  it('starts over once sent', () => {
    const { form } = setup('');
    form.rating.set(5);
    form.contact = 'v@example.com';
    form.consent = true;

    form.submit();

    expect(form.canSend()).toBe(false);
    expect(form.contactValue()).toBe('');
    expect(form.consent).toBe(false);
  });
});
