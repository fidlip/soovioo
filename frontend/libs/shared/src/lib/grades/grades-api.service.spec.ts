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
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 50 });
  });

  it('asks for the page and the filters that are set', () => {
    service
      .getGrades({ studentId: 7, subjectId: null, page: 2, pageSize: 20 })
      .subscribe();
    const req = httpMock.expectOne(
      (r) => r.method === 'GET' && r.url === 'http://test/api/grades',
    );
    expect(req.request.params.toString()).toBe(
      'studentId=7&page=2&pageSize=20',
    );
    req.flush({ items: [], totalCount: 0, page: 2, pageSize: 20 });
  });

  it('fetches one grade by id', () => {
    service.getGrade(12).subscribe();
    const req = httpMock.expectOne('http://test/api/grades/12');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('posts a new grade to the configured base URL', () => {
    service.addGrade({ studentId: 1, subjectId: 1, value: 1 }).subscribe();
    const req = httpMock.expectOne('http://test/api/grades');
    expect(req.request.method).toBe('POST');
    req.flush({
      id: 1,
      studentId: 1,
      studentName: 'Jan Novák',
      schoolClassName: '5.A',
      subjectId: 1,
      subjectName: 'Matematika',
      value: 1,
      gradedOn: '2026-10-18',
      createdAt: new Date().toISOString(),
    });
  });
});
