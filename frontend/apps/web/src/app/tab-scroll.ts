import {
  DestroyRef,
  Directive,
  ElementRef,
  effect,
  inject,
} from '@angular/core';
import { TabList, Tabs } from '@openng/optimus-ui/tabs';

/** Keeps the active tab of a tab bar that doesn't fit in view. The library
 * scrolls the bar only by its arrows and the keyboard, so a tab opened by a
 * link (or one shown half) stayed hidden behind an arrow. Also when the bar
 * gets its width, e.g. in a panel that was hidden. */
// eslint-disable-next-line @angular-eslint/directive-selector -- every tab bar, without marking each
@Directive({ selector: 'p-tablist' })
export class TabScroll {
  private readonly tabList = inject(TabList);
  private readonly host: HTMLElement = inject(ElementRef).nativeElement;

  constructor() {
    const tabs = inject(Tabs);
    effect(() => {
      tabs.value();
      // After the library's own update, which marks the tab active.
      setTimeout(() => this.scrollToActive());
    });

    let width = 0;
    const resize = new ResizeObserver(() => {
      const now = this.host.offsetWidth;
      if (now !== width) {
        width = now;
        this.scrollToActive();
      }
    });
    resize.observe(this.host);
    inject(DestroyRef).onDestroy(() => resize.disconnect());
  }

  private scrollToActive(): void {
    const content: HTMLElement | undefined =
      this.tabList.content?.nativeElement;
    const tab = content?.querySelector<HTMLElement>(
      '[data-pc-name="tab"][data-p-active="true"]',
    );
    if (!content || !tab || content.scrollWidth <= content.clientWidth) {
      return;
    }
    // Centred, so it's clear of the arrows and the veil they cast over the
    // bar's ends; the first and last tab end up at the very start or end,
    // where there's no arrow.
    const bar = content.getBoundingClientRect();
    const box = tab.getBoundingClientRect();
    content.scrollTo({
      left:
        content.scrollLeft +
        box.left +
        box.width / 2 -
        (bar.left + bar.width / 2),
      behavior: 'smooth',
    });
  }
}
