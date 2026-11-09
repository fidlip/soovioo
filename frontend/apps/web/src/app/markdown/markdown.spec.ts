import { renderMarkdown } from './markdown';

describe('renderMarkdown', () => {
  it('formats and keeps line breaks', () => {
    const html = renderMarkdown('**tučně**\n- a\n- b');
    expect(html).toContain('<strong>tučně</strong>');
    expect(html).toContain('<li>a</li>');
  });

  it('drops scripts, handlers and javascript links; links open in a new tab', () => {
    const html = renderMarkdown(
      '<script>alert(1)</script><b onclick="x()">b</b>\n\n[x](javascript:alert(1)) [y](https://soovioo.cz)',
    );
    expect(html).not.toContain('<script');
    expect(html).not.toContain('onclick');
    expect(html).not.toContain('javascript:');
    expect(html).toContain(
      '<a href="https://soovioo.cz" target="_blank" rel="noopener noreferrer">y</a>',
    );
  });
});
