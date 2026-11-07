import { ConfirmationService, MessageService } from '@openng/optimus-ui/api';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  API_BASE_URL,
  Feedback,
  FeedbackState,
  TokenStorage,
} from '@soovioo/shared';
import { UnreadCountsService } from '../../unread-counts.service';
import { FeedbackPage } from './feedback-page';

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

const URL = 'http://test/api/superadmin/feedback';

const feedback = (id: number, extra: Partial<Feedback> = {}) =>
  ({
    id,
    schoolName: 'Demo',
    demoSlug: `demo-${id}`,
    schoolExists: true,
    userName: null,
    rating: 4,
    text: 'Pěkné',
    email: 'v@example.com',
    consent: true,
    role: 'Admin',
    createdAt: '2026-11-07T08:00:00Z',
    seenAt: null,
    flagged: false,
    resolvedAt: null,
    replyCount: 0,
    lastReplyAt: null,
    ...extra,
  }) satisfies Feedback;

describe('FeedbackPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FeedbackPage],
      providers: [
        MessageService,
        ConfirmationService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: new FakeTokenStorage() },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  /** The page's protected handlers, called as the template would. */
  type Page = {
    filterState: FeedbackState | null;
    filterSource: 'School' | 'Demo' | null;
    filterRating: number | null;
    search: string;
    replyText: string;
    onFilterChange(): void;
    clearFilters(): void;
    open(item: Feedback): void;
    markUnseen(item: Feedback): void;
    toggleFlag(item: Feedback): void;
    remove(item: Feedback): void;
    sendReply(item: Feedback): void;
    items(): Feedback[];
    selected(): Feedback | null;
    replies(): { text: string }[] | null;
  };

  const setup = (items: Feedback[]) => {
    const fixture = TestBed.createComponent(FeedbackPage);
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.method === 'GET' && r.url === URL)
      .flush({ items, totalCount: items.length, page: 1, pageSize: 20 });
    fixture.detectChanges();
    return {
      fixture,
      page: fixture.componentInstance as unknown as Page,
    };
  };

  it('lists the feedback with its demo, deleted ones marked', () => {
    const { fixture } = setup([
      feedback(1),
      feedback(2, { schoolExists: false }),
      feedback(3, {
        demoSlug: null,
        schoolName: 'ZŠ Lipová',
        userName: 'Jan Novák',
      }),
    ]);

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll(
      'tbody tr',
    );
    expect(rows[0].textContent).toContain('demo-1');
    expect(rows[0].textContent).not.toContain('(smazáno)');
    expect(rows[1].textContent).toContain('demo-2');
    expect(rows[1].textContent).toContain('(smazáno)');
    expect(rows[0].textContent).toContain('Nové');
    expect(rows[2].textContent).toContain('ZŠ Lipová');
    expect(rows[2].textContent).toContain('Jan Novák');
    expect(rows[2].textContent).not.toContain('demo');
  });

  it('asks the server with the filters, from the first page', () => {
    const { page } = setup([]);

    page.filterState = 'Unanswered';
    page.filterSource = 'School';
    page.filterRating = 0;
    page.search = ' rozvrh ';
    page.onFilterChange();

    const req = httpMock.expectOne((r) => r.url === URL);
    expect(req.request.params.get('state')).toBe('Unanswered');
    expect(req.request.params.get('source')).toBe('School');
    expect(req.request.params.get('rating')).toBe('0');
    expect(req.request.params.get('search')).toBe('rozvrh');
    expect(req.request.params.get('page')).toBe('1');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });

    page.clearFilters();
    const all = httpMock.expectOne((r) => r.url === URL);
    expect(all.request.params.has('state')).toBe(false);
    expect(all.request.params.has('source')).toBe(false);
    expect(all.request.params.has('rating')).toBe(false);
    expect(all.request.params.has('search')).toBe(false);
    all.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('opening a new one marks it seen and refreshes the menu badge', () => {
    const { page } = setup([feedback(1)]);
    const unread = TestBed.inject(UnreadCountsService);

    page.open(page.items()[0]);

    httpMock.expectOne(`${URL}/1/replies`).flush([]);
    const patch = httpMock.expectOne(
      (r) => r.method === 'PATCH' && r.url === `${URL}/1`,
    );
    expect(patch.request.body).toEqual({ seen: true });
    patch.flush(feedback(1, { seenAt: '2026-11-07T09:00:00Z' }));
    httpMock.expectOne(`${URL}/unseen-count`).flush(3);

    expect(page.items()[0].seenAt).toBe('2026-11-07T09:00:00Z');
    expect(page.selected()?.seenAt).toBe('2026-11-07T09:00:00Z');
    expect(unread.feedback()).toBe(3);
  });

  it('opening a seen one changes nothing', () => {
    const { page } = setup([feedback(1, { seenAt: '2026-11-06T09:00:00Z' })]);

    page.open(page.items()[0]);

    httpMock.expectOne(`${URL}/1/replies`).flush([]);
    httpMock.expectNone((r) => r.method === 'PATCH');
  });

  it('marking unseen sends it back and closes the dialog', () => {
    const { page } = setup([feedback(1, { seenAt: '2026-11-06T09:00:00Z' })]);
    page.open(page.items()[0]);
    httpMock.expectOne(`${URL}/1/replies`).flush([]);

    page.markUnseen(page.selected()!);

    const patch = httpMock.expectOne(
      (r) => r.method === 'PATCH' && r.url === `${URL}/1`,
    );
    expect(patch.request.body).toEqual({ seen: false });
    patch.flush(feedback(1));
    httpMock.expectOne(`${URL}/unseen-count`).flush(1);
    expect(page.selected()).toBeNull();
    expect(page.items()[0].seenAt).toBeNull();
  });

  it('flagging updates the row without touching the badge', () => {
    const { page } = setup([feedback(1, { seenAt: '2026-11-06T09:00:00Z' })]);

    page.toggleFlag(page.items()[0]);

    const patch = httpMock.expectOne((r) => r.method === 'PATCH');
    expect(patch.request.body).toEqual({ flagged: true });
    patch.flush(feedback(1, { seenAt: '2026-11-06T09:00:00Z', flagged: true }));
    httpMock.expectNone(`${URL}/unseen-count`);
    expect(page.items()[0].flagged).toBe(true);
  });

  it('a sent reply joins the thread and the row counts it', () => {
    const { page } = setup([feedback(1, { seenAt: '2026-11-06T09:00:00Z' })]);
    page.open(page.items()[0]);
    httpMock.expectOne(`${URL}/1/replies`).flush([]);

    page.replyText = '  Díky!  ';
    page.sendReply(page.selected()!);

    const post = httpMock.expectOne(
      (r) => r.method === 'POST' && r.url === `${URL}/1/replies`,
    );
    expect(post.request.body).toEqual({ text: 'Díky!' });
    post.flush({
      id: 7,
      text: 'Díky!',
      sentBy: 'Jana',
      sentAt: '2026-11-07T10:00:00Z',
    });

    expect(page.replies()?.map((r) => r.text)).toEqual(['Díky!']);
    expect(page.items()[0].replyCount).toBe(1);
    expect(page.items()[0].lastReplyAt).toBe('2026-11-07T10:00:00Z');
    expect(page.replyText).toBe('');
  });

  it('offers the reply only to whoever agreed to be contacted', () => {
    const { fixture, page } = setup([
      feedback(1, { seenAt: '2026-11-06T09:00:00Z', consent: false }),
      feedback(2, { seenAt: '2026-11-06T09:00:00Z' }),
    ]);
    const dialogText = () => document.body.textContent ?? '';

    page.open(page.items()[0]);
    httpMock.expectOne(`${URL}/1/replies`).flush([]);
    fixture.detectChanges();
    expect(dialogText()).toContain('Bez souhlasu nelze odpovědět.');
    expect(document.getElementById('feedbackReply')).toBeNull();

    page.open(page.items()[1]);
    httpMock.expectOne(`${URL}/2/replies`).flush([]);
    fixture.detectChanges();
    expect(document.getElementById('feedbackReply')).not.toBeNull();
  });

  it('deleting asks first, then reloads and refreshes the badge of a new one', () => {
    const { page } = setup([feedback(1), feedback(2)]);
    const confirmation = TestBed.inject(ConfirmationService);
    let accept: (() => void) | undefined;
    vi.spyOn(confirmation, 'confirm').mockImplementation((c) => {
      accept = c.accept as () => void;
      return confirmation;
    });

    page.remove(page.items()[0]);
    httpMock.expectNone((r) => r.method === 'DELETE');
    accept!();

    httpMock
      .expectOne((r) => r.method === 'DELETE' && r.url === `${URL}/1`)
      .flush(null);
    httpMock.expectOne(`${URL}/unseen-count`).flush(1);
    httpMock
      .expectOne((r) => r.method === 'GET' && r.url === URL)
      .flush({ items: [feedback(2)], totalCount: 1, page: 1, pageSize: 20 });
    expect(page.items().map((f) => f.id)).toEqual([2]);
  });
});
