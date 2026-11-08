import { TeachingClass, mainBlockText } from '@soovioo/shared';
import {
  classMainBlockNote,
  mainBlockLessons,
  ownMainBlock,
  resolveMainBlock,
} from './main-block';

describe('main block', () => {
  it('names the periods from–to, or the one', () => {
    expect(mainBlockText({ from: 1, to: 4 })).toBe('1.–4. hodina');
    expect(mainBlockText({ from: 2, to: 2 })).toBe('2. hodina');
  });

  it('takes an end left empty from the level above', () => {
    const school = { from: 1, to: 4 };
    expect(resolveMainBlock(undefined, school)).toEqual(school);
    expect(resolveMainBlock({ from: null, to: 5 }, school)).toEqual({
      from: 1,
      to: 5,
    });
    expect(ownMainBlock({ from: null, to: null })).toBe(false);
    expect(ownMainBlock({ from: null, to: 5 })).toBe(true);
  });

  it('needs each of its periods every school day', () => {
    expect(mainBlockLessons({ from: 1, to: 4 })).toBe(20);
    expect(mainBlockLessons({ from: 3, to: 3 })).toBe(5);
  });

  it('says where a class’s block comes from', () => {
    const c = {
      grade: 1,
      mainBlock: { from: 2, to: 5 },
      inheritedMainBlock: { from: 1, to: 4 },
      ownMainBlock: true,
    } as TeachingClass;

    expect(classMainBlockNote(c)).toBe('vlastní · ročník 1.–4. hodina');
    expect(classMainBlockNote({ ...c, ownMainBlock: false })).toBe(
      'podle ročníku',
    );
    expect(classMainBlockNote({ ...c, grade: null, ownMainBlock: false })).toBe(
      'podle školy',
    );
  });
});
