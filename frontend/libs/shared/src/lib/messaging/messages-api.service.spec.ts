import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { MessagesApiService } from './messages-api.service';

describe('MessagesApiService', () => {
  let service: MessagesApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(MessagesApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists conversations', () => {
    service.getConversations().subscribe();
    const req = httpMock.expectOne('http://test/api/conversations');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('starts a conversation', () => {
    service
      .startConversation({ subject: 'S', body: 'B', studentId: 1 })
      .subscribe();
    const req = httpMock.expectOne('http://test/api/conversations');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ subject: 'S', body: 'B', studentId: 1 });
    req.flush({});
  });

  it('replies to a conversation', () => {
    service.reply(3, 'Díky').subscribe();
    const req = httpMock.expectOne('http://test/api/conversations/3/messages');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'Díky' });
    req.flush({});
  });

  it('marks a conversation read', () => {
    service.markRead(3).subscribe();
    const req = httpMock.expectOne('http://test/api/conversations/3/read');
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('loads contacts for a student', () => {
    service.getContacts(5).subscribe();
    const req = httpMock.expectOne(
      'http://test/api/conversations/contacts?studentId=5',
    );
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('uploads an attachment as multipart', () => {
    const file = new File(['x'], 'a.txt', { type: 'text/plain' });
    service.uploadAttachment(3, 7, file).subscribe();
    const req = httpMock.expectOne(
      'http://test/api/conversations/3/messages/7/attachments',
    );
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush({});
  });

  it('broadcasts to a class', () => {
    service.broadcastToClass(2, { subject: 'S', body: 'B' }).subscribe();
    const req = httpMock.expectOne(
      'http://test/api/school-classes/2/messages/broadcast',
    );
    expect(req.request.method).toBe('POST');
    req.flush({ recipientCount: 0, skippedCount: 0, messages: [] });
  });
});
