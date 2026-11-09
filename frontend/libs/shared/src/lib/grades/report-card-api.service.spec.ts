import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { HalfYearKey, ReportCardApiService } from './report-card-api.service';

describe('ReportCardApiService', () => {
  let service: ReportCardApiService;
  let httpMock: HttpTestingController;
  const half: HalfYearKey = { schoolYearStartYear: 2026, halfYear: 'First' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    });
    service = TestBed.inject(ReportCardApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('asks for the half-year of a class, without empty filters', () => {
    service
      .getReportCards({ ...half, schoolClassId: 3, subjectId: null })
      .subscribe();
    httpMock
      .expectOne(
        'http://test/api/report-cards?schoolYearStartYear=2026&halfYear=First&schoolClassId=3',
      )
      .flush([]);
  });

  it('sets, resets, confirms and reopens a report card grade', () => {
    const key = { studentId: 1, subjectId: 2 };
    service.setGrade(half, key, 'Graded', 1).subscribe();
    service.resetGrade(half, key).subscribe();
    service.confirmGrades(half, [key]).subscribe();
    service.reopenGrade(half, key).subscribe();

    const set = httpMock.expectOne(
      (r) =>
        r.method === 'PUT' && r.url === 'http://test/api/report-cards/grades',
    );
    expect(set.request.body).toEqual({
      ...half,
      ...key,
      kind: 'Graded',
      value: 1,
    });
    set.flush(null);
    httpMock
      .expectOne(
        (r) =>
          r.method === 'DELETE' &&
          r.urlWithParams ===
            'http://test/api/report-cards/grades?schoolYearStartYear=2026&halfYear=First&studentId=1&subjectId=2',
      )
      .flush(null);
    const confirm = httpMock.expectOne(
      'http://test/api/report-cards/grades/confirm',
    );
    expect(confirm.request.body).toEqual({ ...half, grades: [key] });
    confirm.flush({ confirmed: 1, skipped: 0 });
    httpMock
      .expectOne('http://test/api/report-cards/grades/reopen')
      .flush(null);
  });

  it('downloads the class report cards as a blob', () => {
    service.downloadPdf(half, { schoolClassId: 3 }).subscribe();
    const req = httpMock.expectOne(
      'http://test/api/report-cards/pdf?schoolYearStartYear=2026&halfYear=First&schoolClassId=3',
    );
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob());
  });
});
