import { Component, effect, signal } from '@angular/core';
import { Map } from './map/map';
import { ClubDetail } from './club-detail/club-detail';
import { Login } from './login/login';
import { Dashboard } from './dashboard/dashboard';
import { SocPage } from './soc/soc-page/soc-page';
import { SquashClub } from './models/squash-domain';
import { Auth } from './services/auth';
import { ClubData } from './services/club-data';

@Component({
  selector: 'app-root',
  imports: [Map, ClubDetail, Login, Dashboard, SocPage],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly title = signal('squash-frontend');

  // Holds whichever club was last clicked on the map, so it can be passed
  // down into <app-club-detail>. null until the user clicks a marker.
  protected readonly selectedClub = signal<SquashClub | null>(null);

  // Which top-level page is showing: the map+club-detail view, or the
  // Dashboard (reached via the "Dashboard" button in the top bar).
  protected readonly view = signal<'map' | 'dashboard' | 'soc'>('map');

  constructor(
    protected auth: Auth,
    private clubData: ClubData
  ) {
    // An Admin lands on the SOC view as soon as they are logged in.
    effect(() => {
      if (this.auth.currentPlayer()?.role === 'Admin') this.view.set('soc');
    });

    // Keep the selected club in sync whenever ClubData refreshes (Map's
    // initial load, or after accepting/hosting a challenge), so the detail
    // panel reflects the latest backend state without needing a re-click.
    this.clubData.clubs$.subscribe((clubs) => {
      const current = this.selectedClub();
      if (!current) return;
      const updated = clubs.find((c) => c.clubId === current.clubId);
      if (updated) this.selectedClub.set(updated);
    });
  }

  protected onClubSelected(club: SquashClub): void {
    this.selectedClub.set(club);
  }

  protected onDataChanged(): void {
    this.clubData.refresh();
  }

  protected showMap(): void {
    this.view.set('map');
  }

  protected showDashboard(): void {
    this.view.set('dashboard');
  }

  protected showSoc(): void {
    this.view.set('soc');
  }

  protected logout(): void {
    this.auth.logout();
    this.selectedClub.set(null);
    this.view.set('map');
  }
}
