import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { AppVersionService } from './app-version.service';
import { LiveService } from './live.service';

describe('AppVersionService', () => {
  const deployed = (commit: string) => ({
    version: `2026.11.17-${commit}`,
    commit,
    builtAt: '2026-11-17T12:00:00Z',
    repository: 'https://github.com/fidlip/soovioo',
    pullRequests: [],
  });

  function create() {
    const connected = signal(false);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LiveService, useValue: { connected } },
      ],
    });
    const service = TestBed.inject(AppVersionService);
    const http = TestBed.inject(HttpTestingController);
    return { service, http, connected };
  }

  function backToFront() {
    document.dispatchEvent(new Event('visibilitychange'));
  }

  it('remembers the version the tab loaded with', () => {
    const { service, http } = create();
    http.expectOne('/version.json').flush(deployed('aaa'));

    expect(service.info()?.commit).toBe('aaa');
    expect(service.outdated()).toBe(false);
  });

  it('is outdated once another commit is deployed', () => {
    const { service, http } = create();
    http.expectOne('/version.json').flush(deployed('aaa'));

    backToFront();
    http.expectOne('/version.json').flush(deployed('aaa'));
    expect(service.outdated()).toBe(false);

    backToFront();
    http.expectOne('/version.json').flush(deployed('bbb'));
    expect(service.outdated()).toBe(true);

    backToFront();
    http.expectNone('/version.json');
  });

  it('checks when the live connection is back', () => {
    const { service, http, connected } = create();
    http.expectOne('/version.json').flush(deployed('aaa'));

    connected.set(true);
    TestBed.tick();
    http.expectOne('/version.json').flush(deployed('bbb'));

    expect(service.outdated()).toBe(true);
  });

  it('stays quiet when the check fails', () => {
    const { service, http } = create();
    http.expectOne('/version.json').flush(deployed('aaa'));

    backToFront();
    http
      .expectOne('/version.json')
      .flush('', { status: 502, statusText: 'Bad Gateway' });

    expect(service.outdated()).toBe(false);
  });

  it('checks nothing outside a deployed build', () => {
    const { service, http } = create();
    http.expectOne('/version.json').flush({});

    backToFront();
    http.expectNone('/version.json');
    expect(service.info()).toBeNull();
  });
});
