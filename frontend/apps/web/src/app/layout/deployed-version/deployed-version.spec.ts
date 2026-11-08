import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DeployedVersion } from './deployed-version';

describe('DeployedVersion', () => {
  function create() {
    TestBed.configureTestingModule({
      imports: [DeployedVersion],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(DeployedVersion);
    const http = TestBed.inject(HttpTestingController);
    return { fixture, http };
  }

  it('shows the deployed version', () => {
    const { fixture, http } = create();
    http.expectOne('/version.json').flush({
      version: '2026.11.08-abc1234',
      commit: 'abc',
      builtAt: '2026-11-08T20:00:00Z',
      repository: 'https://github.com/fidlip/soovioo',
      pullRequests: [],
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Verze 2026.11.08-abc1234',
    );
  });

  it('shows nothing outside a deployed build', () => {
    const { fixture, http } = create();
    http.expectOne('/version.json').flush({});
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });

  it('shows nothing when version.json is missing', () => {
    const { fixture, http } = create();
    http
      .expectOne('/version.json')
      .flush('', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });
});
