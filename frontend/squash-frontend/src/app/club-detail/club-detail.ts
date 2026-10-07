import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Booking, Player, SquashClub } from '../models/squash-domain';
import { BookingApi } from '../services/booking';
import { Auth } from '../services/auth';

@Component({
  selector: 'app-club-detail',
  imports: [DatePipe],
  templateUrl: './club-detail.html',
  styleUrl: './club-detail.css',
})
export class ClubDetail {
  // Set from App whenever a marker is clicked on the map (see map.ts's
  // clubSelected output). Null until the user has clicked a club.
  @Input() club: SquashClub | null = null;

  // Emitted after any successful write (accepting or hosting a challenge) so
  // App can tell ClubData to refresh - every open view (map popups, this
  // panel) then picks up the new backend state.
  @Output() dataChanged = new EventEmitter<void>();

  protected readonly auth = inject(Auth);
  private readonly bookingApi = inject(BookingApi);

  protected readonly errorMessage = signal<string | null>(null);
  // Holds the id of whichever booking is currently being accepted, so only
  // that one card shows a loading state (and to prevent double-submits).
  protected readonly acceptingId = signal<string | null>(null);

  // "Coaches at this club" is now driven by real registered Coach-role
  // players (not the old static CoachProfile mock list) - booking needs a
  // real backend player id to host a Challenge against.
  protected get coachPlayers(): Player[] {
    return this.club?.players.filter((p) => p.isCoach) ?? [];
  }

  // Whether something is a coaching session or a plain challenge is an
  // explicit flag on the Booking itself (isCoachingSession) - a coach can
  // host either kind (see Dashboard's Type selector), so this can't be
  // inferred from hostPlayer.isCoach alone. Splitting the single
  // activeBookings list here just keeps the two concepts visually distinct
  // without needing a new backend entity.
  protected get openChallenges(): Booking[] {
    return this.club?.activeBookings.filter((b) => !b.isCoachingSession) ?? [];
  }

  protected get coachingSessions(): Booking[] {
    return this.club?.activeBookings.filter((b) => b.isCoachingSession) ?? [];
  }

  // Hides the Accept button on a player's own open bookings - the backend
  // already rejects this with 400 Bad Request, this just avoids the round trip.
  protected isMine(player: Player | undefined): boolean {
    return !!player && player.id === this.auth.currentPlayer()?.playerId;
  }

  protected acceptBooking(booking: Booking): void {
    this.errorMessage.set(null);
    this.acceptingId.set(booking.bookingId);

    this.bookingApi.accept(booking.bookingId).subscribe({
      next: () => {
        this.acceptingId.set(null);
        this.dataChanged.emit();
      },
      error: (err) => {
        this.acceptingId.set(null);
        this.errorMessage.set(this.describeAcceptError(err));
      },
    });
  }

  private describeAcceptError(err: { status?: number; error?: unknown }): string {
    switch (err.status) {
      case 409:
        return 'This was already accepted/booked by someone else.';
      case 503:
        return 'Payment service is currently unavailable. Please try again shortly.';
      case 502:
        return 'Could not reach the payment service after multiple attempts. Please try again.';
      case 402:
        return 'Insufficient funds - add money to your account from the Dashboard and try again.';
      case 400:
        return typeof err.error === 'string' ? err.error : "You can't accept your own booking.";
      default:
        return 'Something went wrong.';
    }
  }
}
