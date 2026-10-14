import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { TimetableApiService } from './timetable-api.service';

describe('TimetableApiService', () => {
  let service: TimetableApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(TimetableApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches a timetable for a school class', () => {
    service.getTimetable(1).subscribe();
    const req = httpMock.expectOne('http://test/api/school-classes/1/timetable');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('adds a timetable entry', () => {
    service
      .addEntry(1, {
        subjectId: 1,
        dayOfWeek: 'Monday',
        periodNumber: 1,
        weekParity: 'Every',
      })
      .subscribe();
    const req = httpMock.expectOne('http://test/api/school-classes/1/timetable');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });
});
