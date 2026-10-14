import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, Grade } from '@sova/shared';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders grades loaded from the API', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const grades: Grade[] = [
      {
        id: 1,
        studentName: 'Jan Novák',
        subject: 'Matematika',
        value: 1,
        createdAt: new Date().toISOString(),
      },
    ];
    httpMock.expectOne('http://test/api/grades').flush(grades);

    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Jan Novák');
  });
});
