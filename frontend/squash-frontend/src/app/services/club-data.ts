import { Service, inject } from '@angular/core';
import { BehaviorSubject, Observable, forkJoin } from 'rxjs';
import { MockData } from './mock-data';
import { PlayersApi } from './players';
import { BookingApi } from './booking';
import { PlayerApiResponse, BookingApiResponse } from '../models/api';
import { Booking, BookingStatus, Player, SquashClub } from '../models/squash-domain';

// Combines real player and booking data (IdentityService + BookingService, both
// via the Gateway) with the static club directory info (name/address/coordinates/
// phone/website/contactPerson/coaches) that still comes from MockData - there's no
// backend model for club directory metadata or coach profiles yet, only for players
// and bookings. This join is a small example of the API Composition pattern: the
// frontend queries two services and stitches their results together for display,
// rather than either service knowing about the other's data.
@Service()
export class ClubData {
  private readonly playersApi = inject(PlayersApi);
  private readonly bookingApi = inject(BookingApi);
  private readonly mockData = inject(MockData);

  // Seeded with the static shells (empty players/bookings) so the map has
  // something to render immediately, before the first real fetch resolves.
  private readonly clubsSubject = new BehaviorSubject<SquashClub[]>(this.mockData.getClubs());
  readonly clubs$: Observable<SquashClub[]> = this.clubsSubject.asObservable();

  get currentClubs(): SquashClub[] {
    return this.clubsSubject.value;
  }

  // Re-fetches players and bookings and rebuilds the club list. Call this on
  // load, and again after any action that changes booking state (e.g. accepting
  // or hosting a challenge or coaching session) so every open view reflects the
  // latest data.
  refresh(): void {
    forkJoin([this.playersApi.getAll(), this.bookingApi.getAll()]).subscribe({
      next: ([players, bookings]) => this.clubsSubject.next(this.buildClubs(players, bookings)),
      error: (err) => {
        console.error('Could not load live player/booking data - showing static club info only.', err);
      },
    });
  }

  private buildClubs(players: PlayerApiResponse[], bookings: BookingApiResponse[]): SquashClub[] {
    const playerById = new Map<string, Player>();
    const playersByClub = new Map<string, Player[]>();

    for (const p of players) {
      const player: Player = {
        id: p.id,
        name: p.name,
        surname: p.surname,
        clubId: p.clubId,
        rank: p.rank,
        isCoach: p.role === 'Coach',
        email: p.email,
        coachBio: p.coachBio ?? undefined,
        coachingLevel: p.coachingLevel ?? undefined,
        hourlyRateZAR: p.hourlyRateZAR ?? undefined,
      };
      playerById.set(p.id, player);

      const list = playersByClub.get(p.clubId) ?? [];
      list.push(player);
      playersByClub.set(p.clubId, list);
    }

    const bookingsByClub = new Map<string, Booking[]>();

    for (const b of bookings) {
      const hostPlayer = playerById.get(b.hostPlayerId);
      if (!hostPlayer) {
        // Shouldn't happen in practice (every booking's host is a registered
        // player), but skip defensively rather than showing a broken card.
        continue;
      }

      const booking: Booking = {
        bookingId: b.id,
        hostPlayer,
        opponent: b.opponentPlayerId ? playerById.get(b.opponentPlayerId) : undefined,
        feeZAR: b.feeZAR,
        status: b.status as BookingStatus,
        proposedLocation: b.proposedLocation ?? undefined,
        isCoachingSession: b.isCoachingSession,
        scheduledAt: b.scheduledAt,
        createdAt: b.createdAt,
      };

      const list = bookingsByClub.get(b.clubId) ?? [];
      list.push(booking);
      bookingsByClub.set(b.clubId, list);
    }

    return this.mockData.getClubs().map((shell) => ({
      ...shell,
      players: playersByClub.get(shell.clubId) ?? [],
      activeBookings: bookingsByClub.get(shell.clubId) ?? [],
      // coaches intentionally left as-is - see class comment above.
    }));
  }
}
