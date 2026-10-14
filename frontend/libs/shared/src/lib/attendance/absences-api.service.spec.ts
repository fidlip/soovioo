import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { AbsencesApiService } from './absences-api.service';

describe('AbsencesApiService', () => {
  let service: AbsencesApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(AbsencesApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches absences', () => {
    service.getAbsences().subscribe();
    const req = httpMock.expectOne('http://test/api/absences');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('creates an absence', () => {
    service.addAbsence({ studentId: 1, date: '2026-10-10' }).subscribe();
    const req = httpMock.expectOne('http://test/api/absences');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('submits an excuse reason', () => {
    service.submitExcuse(1, 'Nemoc').subscribe();
    const req = httpMock.expectOne('http://test/api/absences/1/excuse');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'Nemoc' });
    req.flush(null);
  });

  it('reviews an absence', () => {
    service.reviewAbsence(1, true).subscribe();
    const req = httpMock.expectOne('http://test/api/absences/1/review');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ approve: true });
    req.flush(null);
  });
});
