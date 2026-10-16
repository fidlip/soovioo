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

  it('fetches messages for a student', () => {
    service.getMessages(1).subscribe();
    const req = httpMock.expectOne('http://test/api/students/1/messages');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('sends a message', () => {
    service.sendMessage(1, { subject: 'S', body: 'B' }).subscribe();
    const req = httpMock.expectOne('http://test/api/students/1/messages');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('marks a message as read', () => {
    service.markRead(1, 2).subscribe();
    const req = httpMock.expectOne('http://test/api/students/1/messages/2/read');
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('fetches broadcastable classes', () => {
    service.getBroadcastableClasses().subscribe();
    const req = httpMock.expectOne('http://test/api/messages/broadcastable-classes');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('broadcasts to a class', () => {
    service.broadcastToClass(1, { subject: 'S', body: 'B' }).subscribe();
    const req = httpMock.expectOne('http://test/api/school-classes/1/messages/broadcast');
    expect(req.request.method).toBe('POST');
    req.flush({ recipientCount: 2 });
  });

  it('broadcasts to the whole school', () => {
    service.broadcastToSchool({ subject: 'S', body: 'B' }).subscribe();
    const req = httpMock.expectOne('http://test/api/messages/broadcast');
    expect(req.request.method).toBe('POST');
    req.flush({ recipientCount: 5 });
  });
});
