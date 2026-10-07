// Shapes returned by the real backend (IdentityService and BookingService, both
// reached through the ApiGateway). These mirror the C# response DTOs field-for-field
// using ASP.NET Core's default camelCase JSON naming.
//
// Kept separate from squash-domain.ts because these are the *wire* shapes - the
// UI-facing Player/Booking/SquashClub types in squash-domain.ts stay as they
// are, and ClubData (services/club-data.ts) is what translates between the two.

export interface PlayerApiResponse {
  id: string;
  name: string;
  surname: string;
  email: string;
  clubId: string;
  rank: string;
  role: string; // 'Player' | 'Coach' | 'Admin'
  coachBio: string | null;
  coachingLevel: string | null;
  hourlyRateZAR: number | null;
}

export interface BookingApiResponse {
  id: string;
  clubId: string;
  hostPlayerId: string;
  opponentPlayerId: string | null;
  feeZAR: number;
  status: string; // 'Open' | 'Accepted' | 'Completed' | 'Cancelled'
  proposedLocation: string | null;
  isCoachingSession: boolean;
  scheduledAt: string;
  createdAt: string;
}

// Mirrors IdentityService's UpdatePlayerRequest/ChangePasswordRequest DTOs.
export interface UpdatePlayerApiRequest {
  name: string;
  surname: string;
  email: string;
  clubId: string;
  rank: string;
  coachBio: string | null;
  coachingLevel: string | null;
  hourlyRateZAR: number | null;
}

export interface ChangePasswordApiRequest {
  currentPassword: string;
  newPassword: string;
}

// Mirrors PaymentService's WalletResponse/DepositRequest DTOs.
export interface WalletApiResponse {
  balanceZAR: number;
}

export interface DepositApiRequest {
  amountZAR: number;
}
