// src/app/models/squash-domain.ts
//
// Shared domain types for the SA Squash challenge & coaching platform.
// These are written to match the shape the future API Gateway / microservices
// (Identity, Booking & Matchmaking, Escrow & Payment) will eventually return,
// so swapping MockDataService for real HTTP calls later shouldn't require
// changing the rest of the frontend.

export interface Player {
  id: string;
  name: string;
  // Optional for the same reason as email below - the static mock roster only
  // ever set a single full "name" per player. Always populated for real,
  // backend-sourced players, where it holds the actual surname captured at
  // registration (see ClubData.buildClubs).
  surname?: string;
  clubId: string;
  rank: string; // Mocked SportyHQ rank for now, e.g. "A", "B1", "Unranked"
  isCoach: boolean;
  // Optional because the static mock roster (services/mock-data.ts) doesn't
  // have one - always populated for real, backend-sourced players (see
  // ClubData.buildClubs), which is the only place this is actually shown
  // (the "contact the host" line on an accepted booking).
  email?: string;
  // Coach-only profile fields (real, backend-sourced - undefined for regular
  // players, and possibly undefined for a coach who hasn't filled them in yet).
  coachBio?: string;
  coachingLevel?: string;
  hourlyRateZAR?: number;
}

export interface CoachProfile {
  coachId: string;
  player: Player;
  bio: string;
  coachingLevel: string; // e.g. "SA Squash Level 2 Coach"
  hourlyRateZAR: number;
  contactEmail: string;
}

export type BookingStatus = 'Open' | 'Accepted' | 'Completed' | 'Cancelled';

export interface Booking {
  bookingId: string;
  hostPlayer: Player;
  opponent?: Player;
  feeZAR: number;
  status: BookingStatus;
  proposedLocation?: string;
  // Explicit host choice, not derived from hostPlayer.isCoach - a coach can
  // host a plain Challenge too, so this is the only reliable way to tell
  // which list (Open Challenges vs Coaching Sessions) something belongs in.
  isCoachingSession: boolean;
  scheduledAt: string; // ISO date string - when the session/challenge is due to be played
  createdAt: string; // ISO date string
}

export interface Coordinates {
  lat: number;
  lng: number;
}

export interface SquashClub {
  clubId: string;
  name: string;
  address: string;
  coordinates: Coordinates;
  players: Player[];
  coaches: CoachProfile[];
  activeBookings: Booking[];
  // Real-world contact details, researched from NW Squash / club sources.
  // Optional because most small-town clubs don't have all of these published.
  phone?: string;
  website?: string;
  contactPerson?: string;
}
