import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { GradesApiService } from './grades-api.service';

describe('GradesApiService', () => {
  let service: GradesApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(GradesApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches grades from the configured base URL', () => {
    service.getGrades().subscribe();
    const req = httpMock.expectOne('http://test/api/grades');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('posts a new grade to the configured base URL', () => {
    service
      .addGrade({ studentName: 'Jan Novák', subject: 'Matematika', value: 1 })
      .subscribe();
    const req = httpMock.expectOne('http://test/api/grades');
    expect(req.request.method).toBe('POST');
    req.flush({
      id: 1,
      studentName: 'Jan Novák',
      subject: 'Matematika',
      value: 1,
      createdAt: new Date().toISOString(),
    });
  });
});
