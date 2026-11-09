import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@soovioo/shared';
import { ReportCardTab, StudentOption } from './report-card-tab';

describe('ReportCardTab', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReportCardTab],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    // jsdom cannot follow the download link.
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {
      /* no navigation */
    });
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  const render = (students: StudentOption[]) => {
    const fixture = TestBed.createComponent(ReportCardTab);
    fixture.componentRef.setInput('students', students);
    fixture.detectChanges();
    return fixture;
  };

  const downloadButton = (element: HTMLElement) =>
    [...element.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('Stáhnout vysvědčení'),
    ) as HTMLButtonElement;

  it('with one pupil downloads their report card without asking whose', () => {
    const fixture = render([{ id: 7, label: 'Jan Novák' }]);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('#reportCardStudent')).toBeNull();
    downloadButton(element).click();

    httpMock
      .expectOne('http://test/api/students/7/report-card')
      .flush(new Blob());
  });

  it('with several pupils offers a choice, the first one picked', () => {
    const fixture = render([
      { id: 7, label: 'Jan Novák' },
      { id: 8, label: 'Eva Nováková' },
    ]);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('Vysvědčení pro');
    downloadButton(element).click();

    httpMock
      .expectOne('http://test/api/students/7/report-card')
      .flush(new Blob());
  });
});
