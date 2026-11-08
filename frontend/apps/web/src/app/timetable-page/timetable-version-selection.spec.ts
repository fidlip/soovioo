import { TestBed } from '@angular/core/testing';
import { TimetableVersion, TimetableVersionsApiService } from '@soovioo/shared';
import { of } from 'rxjs';
import { TimetableVersionSelection } from './timetable-version-selection';

function version(
  id: number,
  overrides: Partial<TimetableVersion> = {},
): TimetableVersion {
  return {
    id,
    name: `Verze ${id}`,
    validFrom: '2000-01-01',
    validTo: null,
    status: 'Draft',
    entryCount: 0,
    createdAt: '2000-01-01T00:00:00Z',
    publishedAt: null,
    copiedFromVersionId: null,
    copiedFromName: null,
    ...overrides,
  };
}

describe('TimetableVersionSelection', () => {
  let versions: TimetableVersion[];
  let selection: TimetableVersionSelection;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TimetableVersionSelection,
        {
          provide: TimetableVersionsApiService,
          useValue: { getVersions: () => of(versions) },
        },
      ],
    });
    selection = TestBed.inject(TimetableVersionSelection);
  });

  it('starts on the version valid today', () => {
    versions = [version(1), version(2, { status: 'Published' })];
    selection.reload();
    expect(selection.versionId()).toBe(2);
    expect(selection.loaded()).toBe(true);
  });

  it('starts on the first one where none is valid today', () => {
    versions = [version(1), version(2)];
    selection.reload();
    expect(selection.versionId()).toBe(1);
  });

  it('keeps the chosen one, and leaves it once it is gone', () => {
    versions = [version(1), version(2, { status: 'Published' })];
    selection.versionId.set(1);
    selection.reload();
    expect(selection.versionId()).toBe(1);

    versions = [version(2, { status: 'Published' })];
    selection.reload();
    expect(selection.versionId()).toBe(2);
  });

  it('has no version at a school without a timetable', () => {
    versions = [];
    selection.reload();
    expect(selection.versionId()).toBeNull();
    expect(selection.loaded()).toBe(true);
  });
});
