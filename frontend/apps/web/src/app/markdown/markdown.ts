import { Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import DOMPurify from 'dompurify';
import { marked } from 'marked';

DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A') {
    node.setAttribute('target', '_blank');
    node.setAttribute('rel', 'noopener noreferrer');
  }
});

/** Markdown → safe HTML: no raw scripts, styles or event handlers survive
 * (DOMPurify); links open in a new tab. */
export function renderMarkdown(text: string): string {
  const html = marked.parse(text, { async: false, gfm: true, breaks: true });
  return DOMPurify.sanitize(html, { FORBID_TAGS: ['style', 'img'] });
}

/** Text written in Markdown, rendered. */
@Component({
  selector: 'app-markdown',
  template: `<div class="markdown" [innerHTML]="html()"></div>`,
  styleUrl: './markdown.css',
  host: { class: 'block' },
})
export class Markdown {
  private readonly sanitizer = inject(DomSanitizer);

  readonly text = input.required<string | null>();

  protected readonly html = computed<SafeHtml>(() =>
    // Sanitized by DOMPurify already — Angular's own pass would only warn.
    this.sanitizer.bypassSecurityTrustHtml(renderMarkdown(this.text() ?? '')),
  );
}
