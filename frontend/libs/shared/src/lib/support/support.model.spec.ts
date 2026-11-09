import { approvalDuration } from './support.model';

describe('approvalDuration', () => {
  it('says hours when whole, minutes otherwise, in the right form', () => {
    expect(approvalDuration(24 * 60)).toBe('24 hodin');
    expect(approvalDuration(60)).toBe('1 hodinu');
    expect(approvalDuration(120)).toBe('2 hodiny');
    expect(approvalDuration(2)).toBe('2 minuty');
    expect(approvalDuration(1)).toBe('1 minutu');
    expect(approvalDuration(30)).toBe('30 minut');
  });
});
