import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { Auth } from '../services/auth';
import { ClubData } from '../services/club-data';
import { PlayersApi } from '../services/players';
import { BookingApi } from '../services/booking';
import { WalletApi } from '../services/wallet';
import { MockData } from '../services/mock-data';
import { Booking, SquashClub } from '../models/squash-domain';

// The Dashboard page - reachable via the "Dashboard" button in the top bar
// (see app.ts/app.html). Left side: edit-my-profile + change-password.
// Right side: challenges/sessions this player hosted ("My Challenges &
// Coaching Sessions") and ones they've accepted/booked as the opponent
// ("Accepted Challenges & Coaching Sessions"). Both lists are built from
// ClubData.clubs$, which already joins real player + booking data across
// every club - no new backend endpoint needed for either list.
@Component({
  selector: 'app-dashboard',
  imports: [FormsModule, DatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css',
})
export class Dashboard {
  private readonly auth = inject(Auth);
  private readonly clubData = inject(ClubData);
  private readonly playersApi = inject(PlayersApi);
  private readonly bookingApi = inject(BookingApi);
  private readonly walletApi = inject(WalletApi);
  private readonly mockData = inject(MockData);

  protected readonly clubs: SquashClub[] = this.mockData.getClubs();

  protected readonly isLoadingProfile = signal(true);
  protected readonly isSavingProfile = signal(false);
  protected readonly profileError = signal<string | null>(null);
  protected readonly profileSuccess = signal<string | null>(null);

  protected readonly isSavingPassword = signal(false);
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordSuccess = signal<string | null>(null);

  protected readonly myHosted = signal<Booking[]>([]);
  protected readonly myAccepted = signal<Booking[]>([]);

  // Moved here from the club-detail panel - hosting a challenge/coaching
  // session always targets the player's own registered club (see isMyClub's
  // old comment in club-detail.ts), so it doesn't need a selected club at all.
  protected readonly isHosting = signal(false);
  protected readonly hostError = signal<string | null>(null);
  protected newBookingFeeZAR = 100;
  // Bound to a native <input type="date">, so this is always either '' or a
  // 'yyyy-MM-dd' string - min="today" on the input keeps the picker itself
  // from offering past dates, and hostBooking() re-checks it's actually set
  // before submitting (the backend also rejects a past date server-side).
  protected newBookingScheduledAt = '';
  protected readonly today = new Date().toISOString().slice(0, 10);

  // Only a coach sees this choice at all (see dashboard.html's @if(isCoach) on
  // the Type field) - a regular player can only ever host a plain Challenge.
  // A coach can host either kind, so this can't just be inferred from their
  // role - it needs to be an explicit choice, sent as isCoachingSession on
  // the create request. The backend re-checks the Coach role server-side too
  // (see BookingController.Create), so this is purely a UI convenience.
  protected hostType: 'challenge' | 'coaching' = 'challenge';

  // Balance card + "Add Funds" modal. This is a demo top-up flow, not a real
  // payment integration - the card-entry form below never leaves the browser
  // (see the deposit() call, which only ever sends the amount), so there's
  // nothing here to validate as a real card. It exists to demonstrate that
  // accepting a challenge/session and topping up both go through the same
  // PaymentService wallet.
  protected readonly walletBalance = signal<number | null>(null);
  protected readonly isDepositModalOpen = signal(false);
  protected readonly isProcessingDeposit = signal(false);
  protected readonly depositError = signal<string | null>(null);

  protected depositAmount = 100;
  protected cardNumber = '';
  protected cardExpiry = '';
  protected cardCvc = '';
  protected cardholderName = '';
  protected country = 'ZA';

  protected name = '';
  protected surname = '';
  protected email = '';
  protected clubId = '';
  protected coachBio = '';
  protected coachingLevel = '';
  protected hourlyRateZAR: number | null = null;

  // Preserved from the loaded profile and echoed back unchanged on save -
  // Rank isn't editable here (it's the mocked SportyHQ rank), so this just
  // avoids the Update endpoint accidentally clobbering it.
  private currentRank = 'Unranked';

  protected currentPassword = '';
  protected newPassword = '';
  protected confirmNewPassword = '';

  protected get isCoach(): boolean {
    return this.auth.currentPlayer()?.role === 'Coach';
  }

  // Uses the session's clubId (not the possibly-unsaved this.clubId edit
  // field above) so hosting always targets the club the player is actually
  // registered at right now.
  protected get myClubName(): string {
    const clubId = this.auth.currentPlayer()?.clubId;
    return this.clubs.find((c) => c.clubId === clubId)?.name ?? 'your club';
  }

  constructor() {
    const current = this.auth.currentPlayer();
    if (current) {
      this.playersApi.getById(current.playerId).subscribe({
        next: (player) => {
          this.name = player.name;
          this.surname = player.surname;
          this.email = player.email;
          this.clubId = player.clubId;
          this.currentRank = player.rank;
          this.coachBio = player.coachBio ?? '';
          this.coachingLevel = player.coachingLevel ?? '';
          this.hourlyRateZAR = player.hourlyRateZAR;
          this.isLoadingProfile.set(false);
        },
        error: () => {
          this.profileError.set('Could not load your profile - is the backend running?');
          this.isLoadingProfile.set(false);
        },
      });
    }

    this.clubData.clubs$.subscribe((clubs) => this.rebuildBookingLists(clubs));
    this.clubData.refresh();

    this.refreshWalletBalance();
  }

  private refreshWalletBalance(): void {
    this.walletApi.getBalance().subscribe({
      next: (wallet) => this.walletBalance.set(wallet.balanceZAR),
      error: () => {
        // Leave it as null (renders as "—") rather than surfacing a separate
        // error banner for a card that's secondary to the rest of the page.
      },
    });
  }

  private rebuildBookingLists(clubs: SquashClub[]): void {
    const myId = this.auth.currentPlayer()?.playerId;
    if (!myId) return;

    const all = clubs.flatMap((c) => c.activeBookings);
    this.myHosted.set(all.filter((c) => c.hostPlayer.id === myId));
    this.myAccepted.set(all.filter((c) => c.opponent?.id === myId));
  }

  protected saveProfile(): void {
    const current = this.auth.currentPlayer();
    if (!current) return;

    this.profileError.set(null);
    this.profileSuccess.set(null);
    this.isSavingProfile.set(true);

    this.playersApi
      .update(current.playerId, {
        name: this.name,
        surname: this.surname,
        email: this.email,
        clubId: this.clubId,
        rank: this.currentRank,
        coachBio: this.isCoach ? this.coachBio || null : null,
        coachingLevel: this.isCoach ? this.coachingLevel || null : null,
        hourlyRateZAR: this.isCoach ? this.hourlyRateZAR : null,
      })
      .subscribe({
        next: (updated) => {
          this.isSavingProfile.set(false);
          this.profileSuccess.set('Profile updated.');
          this.auth.updateStoredProfile({ name: updated.name, surname: updated.surname, clubId: updated.clubId });
          this.clubData.refresh();
        },
        error: (err) => {
          this.isSavingProfile.set(false);
          this.profileError.set(
            err.status === 409 ? 'That email is already in use by another account.' : 'Could not update your profile.'
          );
        },
      });
  }

  protected openDepositModal(): void {
    this.depositError.set(null);
    this.isDepositModalOpen.set(true);
  }

  protected closeDepositModal(): void {
    this.isDepositModalOpen.set(false);
  }

  protected submitDeposit(): void {
    this.depositError.set(null);

    if (!this.depositAmount || this.depositAmount <= 0) {
      this.depositError.set('Enter an amount greater than zero.');
      return;
    }
    // Light client-side shape checks only - this never becomes a real card
    // charge, so there's no need for real card validation (Luhn, expiry, etc).
    if (this.cardNumber.replace(/\s/g, '').length < 12 || !this.cardExpiry || !this.cardCvc || !this.cardholderName) {
      this.depositError.set('Fill in all card details to continue.');
      return;
    }

    this.isProcessingDeposit.set(true);

    this.walletApi.deposit({ amountZAR: this.depositAmount }).subscribe({
      next: (wallet) => {
        this.isProcessingDeposit.set(false);
        this.walletBalance.set(wallet.balanceZAR);
        this.isDepositModalOpen.set(false);
        this.cardNumber = '';
        this.cardExpiry = '';
        this.cardCvc = '';
        this.cardholderName = '';
      },
      error: () => {
        this.isProcessingDeposit.set(false);
        this.depositError.set('Could not add funds - is the backend running?');
      },
    });
  }

  protected hostBooking(): void {
    const current = this.auth.currentPlayer();
    if (!current) return;

    this.hostError.set(null);

    if (!this.newBookingScheduledAt) {
      this.hostError.set('Pick a date for the challenge or session.');
      return;
    }
    if (this.newBookingScheduledAt < this.today) {
      this.hostError.set('The date must be today or later.');
      return;
    }

    this.isHosting.set(true);

    const isCoachingSession = this.isCoach && this.hostType === 'coaching';

    this.bookingApi
      .create({
        clubId: current.clubId,
        feeZAR: this.newBookingFeeZAR,
        scheduledAt: this.newBookingScheduledAt,
        isCoachingSession,
      })
      .subscribe({
        next: () => {
          this.isHosting.set(false);
          this.newBookingScheduledAt = '';
          this.clubData.refresh();
        },
        error: (err) => {
          this.isHosting.set(false);
          this.hostError.set(
            err.status === 403
              ? 'Only coaches can post coaching sessions.'
              : 'Could not create the booking - is the backend running?'
          );
        },
      });
  }

  protected changePassword(): void {
    const current = this.auth.currentPlayer();
    if (!current) return;

    this.passwordError.set(null);
    this.passwordSuccess.set(null);

    if (!this.newPassword || this.newPassword !== this.confirmNewPassword) {
      this.passwordError.set('New passwords do not match.');
      return;
    }

    this.isSavingPassword.set(true);

    this.playersApi
      .changePassword(current.playerId, {
        currentPassword: this.currentPassword,
        newPassword: this.newPassword,
      })
      .subscribe({
        next: () => {
          this.isSavingPassword.set(false);
          this.passwordSuccess.set('Password changed.');
          this.currentPassword = '';
          this.newPassword = '';
          this.confirmNewPassword = '';
        },
        error: (err) => {
          this.isSavingPassword.set(false);
          this.passwordError.set(err.status === 401 ? 'Current password is incorrect.' : 'Could not change your password.');
        },
      });
  }
}
