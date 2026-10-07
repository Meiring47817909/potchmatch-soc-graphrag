import { AfterViewInit, Component, EventEmitter, Output } from '@angular/core';
import * as L from 'leaflet';
import { MockData } from '../services/mock-data';
import { ClubData } from '../services/club-data';
import { SquashClub } from '../models/squash-domain';

// Leaflet's default marker icon points at image URLs that are relative to
// leaflet's own JS file (e.g. "images/marker-icon.png") - that works when
// leaflet is loaded as a plain <script>, but Angular's build bundles the JS
// somewhere else entirely, so those relative paths 404 and the browser shows
// its broken-image icon instead of a marker. The fix is to copy leaflet's
// marker images into public/leaflet/ (served from the site root - see
// angular.json's "assets" glob on the public/ folder) and point the default
// icon at those instead. This only needs to run once, so it's done at module
// load time rather than inside the component.
delete (L.Icon.Default.prototype as unknown as { _getIconUrl?: unknown })._getIconUrl;
L.Icon.Default.mergeOptions({
  iconRetinaUrl: 'leaflet/marker-icon-2x.png',
  iconUrl: 'leaflet/marker-icon.png',
  shadowUrl: 'leaflet/marker-shadow.png',
});

@Component({
  selector: 'app-map',
  imports: [],
  templateUrl: './map.html',
  styleUrl: './map.css',
})
export class Map implements AfterViewInit {
  private map!: L.Map;
  private markers: L.Marker[] = [];

  // Fires whenever a club marker is clicked, so a parent component (App)
  // can display that club's full detail without the Map component needing
  // to know anything about how that detail view is laid out.
  @Output() clubSelected = new EventEmitter<SquashClub>();

  constructor(
    private mockData: MockData,
    private clubData: ClubData
  ) {}

  ngAfterViewInit(): void {
    // Leaflet needs the #map div to exist in the DOM first, which is why
    // this runs in ngAfterViewInit rather than the constructor or ngOnInit.
    // Bounds/tiles only need the static club shells (coordinates never change),
    // so the base map can be built before any HTTP call resolves.
    const shells = this.mockData.getClubs();

    // Compute a bounding box that tightly wraps all 7 clubs, then pad it
    // outward a bit so markers near the edge aren't jammed against the
    // screen border. Using .pad() instead of hardcoded coordinates means
    // this keeps working correctly if clubs are added/removed later.
    const clubBounds = L.latLngBounds(shells.map((club) => club.coordinates));
    const maxBounds = clubBounds.pad(0.4); // 40% extra breathing room

    this.map = L.map('map', {
      maxBounds, // the "world border" - the user can't pan outside this box
      maxBoundsViscosity: 1.0, // 1.0 = hard stop at the edge, no elastic bounce-back
    });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
    }).addTo(this.map);

    // Frame the initial view around the clubs themselves, not all of South Africa.
    this.map.fitBounds(clubBounds, { padding: [40, 40] });

    // maxBounds only stops panning - without a minZoom, the user could still
    // zoom out past the point where maxBounds fills the screen and see grey
    // empty space outside it. getBoundsZoom() finds the most-zoomed-out level
    // where maxBounds still exactly fills the map container, and we lock that
    // in as the floor, so zooming out just stops instead of showing empty space.
    this.map.setMinZoom(this.map.getBoundsZoom(maxBounds));

    // Real player/booking counts come from the backend. Subscribe first so
    // markers are (re)drawn every time the data changes, then kick off the load.
    this.clubData.clubs$.subscribe((clubs) => this.renderMarkers(clubs));
    this.clubData.refresh();
  }

  private renderMarkers(clubs: SquashClub[]): void {
    this.markers.forEach((marker) => marker.remove());
    this.markers = [];

    clubs.forEach((club) => {
      const marker = L.marker([club.coordinates.lat, club.coordinates.lng]).addTo(this.map);

      marker.bindPopup(`
        <strong>${club.name}</strong><br>
        ${club.address}<br>
        ${club.players.length} players · ${club.coaches.length} coaches · ${club.activeBookings.length} open challenges
        `);

      marker.on('click', () => this.clubSelected.emit(club));
      this.markers.push(marker);
    });
  }
}
