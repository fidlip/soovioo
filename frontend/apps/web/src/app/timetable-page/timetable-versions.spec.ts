import { TimetableVersion } from '@soovioo/shared';
import { versionSource } from './timetable-versions';

function version(
  id: number,
  overrides: Partial<TimetableVersion> = {},
): TimetableVersion {
  return {
    id,
    name: `Verze ${id}`,
    validFrom: '2026-09-01',
    validTo: null,
    status: 'Draft',
    entryCount: 0,
    createdAt: '2026-01-01T00:00:00Z',
    publishedAt: null,
    copiedFromVersionId: null,
    copiedFromName: null,
    ...overrides,
  };
}

describe('versionSource', () => {
  it('names the source as it is now', () => {
    const source = version(1, { name: 'Přejmenovaná' });
    const copy = version(2, {
      copiedFromVersionId: 1,
      copiedFromName: 'Stará',
    });

    expect(versionSource(copy, [source, copy])).toBe(
      'Vychází z „Přejmenovaná“',
    );
  });

  it('keeps the name of a deleted draft', () => {
    const copy = version(2, { copiedFromName: 'Smazaná' });

    expect(versionSource(copy, [copy])).toBe(
      'Vychází ze smazaného konceptu „Smazaná“',
    );
  });

  it('names a source not listed, as it was', () => {
    const copy = version(2, {
      copiedFromVersionId: 1,
      copiedFromName: 'Koncept',
    });

    expect(versionSource(copy, [copy])).toBe('Vychází z „Koncept“');
  });

  it('says nothing of an empty one', () => {
    expect(versionSource(version(2), [])).toBeNull();
  });
});
