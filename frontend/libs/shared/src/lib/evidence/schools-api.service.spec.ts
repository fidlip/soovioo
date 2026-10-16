import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { SchoolsApiService } from './schools-api.service';

describe('SchoolsApiService', () => {
  let service: SchoolsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(SchoolsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches a page of schools with the default page/pageSize', () => {
    service.getSchools().subscribe();
    const req = httpMock.expectOne('http://test/api/schools?page=1&pageSize=20');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('fetches a specific page of schools', () => {
    service.getSchools(2, 10).subscribe();
    const req = httpMock.expectOne('http://test/api/schools?page=2&pageSize=10');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], totalCount: 0, page: 2, pageSize: 10 });
  });

  it('includes the search param only when given', () => {
    service.getSchools(1, 20, 'demo').subscribe();
    const req = httpMock.expectOne('http://test/api/schools?page=1&pageSize=20&search=demo');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });
});
