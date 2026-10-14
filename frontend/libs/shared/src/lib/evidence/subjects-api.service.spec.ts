import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { SubjectsApiService } from './subjects-api.service';

describe('SubjectsApiService', () => {
  let service: SubjectsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(SubjectsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches subjects from the configured base URL', () => {
    service.getSubjects().subscribe();
    const req = httpMock.expectOne('http://test/api/subjects');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });
});
