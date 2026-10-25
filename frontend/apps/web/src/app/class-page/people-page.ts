import { Component, signal } from '@angular/core';
import {
  Tab,
  TabList,
  TabPanel,
  TabPanels,
  Tabs,
} from '@openng/optimus-ui/tabs';
import { AppIcon } from '../app-icon';
import { ClassPage } from './class-page';
import { SchoolDirectoryList } from './school-directory';

/** Lidé for parents and pupils: the class (Třída) and everyone at the
 * school (Škola), each as far as "Viditelnost údajů" lets them be seen. */
@Component({
  selector: 'app-people-page',
  imports: [
    Tabs,
    TabList,
    Tab,
    TabPanels,
    TabPanel,
    AppIcon,
    ClassPage,
    SchoolDirectoryList,
  ],
  templateUrl: './people-page.html',
})
export class PeoplePage {
  protected readonly activeTab = signal('class');
}
