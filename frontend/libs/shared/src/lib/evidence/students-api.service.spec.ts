import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { StudentsApiService } from './students-api.service';

describe('StudentsApiService', () => {
  let service: StudentsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(StudentsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches students from the configured base URL', () => {
    service.getStudents().subscribe();
    const req = httpMock.expectOne('http://test/api/students');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('asks whom a permission reaches', () => {
    service.getReach('GradesWrite').subscribe();
    const req = httpMock.expectOne(
      'http://test/api/students/reach?permission=GradesWrite',
    );
    expect(req.request.method).toBe('GET');
    req.flush({ all: true, students: [] });
  });
});
