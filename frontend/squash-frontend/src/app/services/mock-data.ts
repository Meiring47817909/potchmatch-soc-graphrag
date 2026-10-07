import { Service } from '@angular/core';
import { SquashClub, Player, CoachProfile, Booking } from '../models/squash-domain';

// Mock data for the 7 NW Squash-affiliated clubs we're focusing on for this
// project (Pukke and the Boys High school club were dropped since they're
// not standalone public clubs). Club contact details and addresses were
// researched from nwsquash.co.za/features/affiliated-clubs and each club's
// own public presence (website, SportyHQ, Facebook, local news) where
// available. Every player, coach and challenge below is fictional - real
// player rosters aren't publicly available (SportyHQ blocks scraping and
// requires a formal API request), and even if they were, it's not
// appropriate to attach real people's names to invented financial
// transactions in a demo. Each club gets one coach and 2-3 players.

@Service()
export class MockData {
  private readonly players: Player[] = [
    // Potchefstroom Country Club (PCC)
    { id: 'p1', name: 'Sarah Naidoo', clubId: 'pcc', rank: 'A', isCoach: false },
    { id: 'p2', name: 'Kyle Botha', clubId: 'pcc', rank: 'B1', isCoach: false },
    { id: 'p3', name: 'Werner Steyn', clubId: 'pcc', rank: 'B2', isCoach: false },
    { id: 'p4', name: 'David Reddy', clubId: 'pcc', rank: 'Unranked', isCoach: true },

    // Potchefstroom Defence Squash Club
    { id: 'p5', name: 'Michael Erasmus', clubId: 'defence', rank: 'B1', isCoach: false },
    { id: 'p6', name: 'Riaan Coetzee', clubId: 'defence', rank: 'Unranked', isCoach: true },

    // Klerksdorp Squash Club
    { id: 'p7', name: 'Thabo Mokoena', clubId: 'klerksdorp', rank: 'A', isCoach: false },
    { id: 'p8', name: 'Tumi Sithole', clubId: 'klerksdorp', rank: 'B2', isCoach: false },
    { id: 'p9', name: 'Lindiwe Dlamini', clubId: 'klerksdorp', rank: 'A', isCoach: true },

    // Rustenburg Squash Club
    { id: 'p10', name: 'Emma van der Merwe', clubId: 'rustenburg', rank: 'B2', isCoach: false },
    { id: 'p11', name: 'Karabo Mahlangu', clubId: 'rustenburg', rank: 'A', isCoach: false },
    { id: 'p12', name: 'Pieter Human', clubId: 'rustenburg', rank: 'Unranked', isCoach: true },

    // Coligny Squash Club
    { id: 'p13', name: 'Andre Venter', clubId: 'coligny', rank: 'B1', isCoach: false },
    { id: 'p14', name: 'Nomvula Khumalo', clubId: 'coligny', rank: 'B2', isCoach: false },
    { id: 'p15', name: 'Anja Fourie', clubId: 'coligny', rank: 'Unranked', isCoach: true },

    // Fochville Squash Club
    { id: 'p16', name: 'Liam Mostert', clubId: 'fochville', rank: 'B2', isCoach: false },
    { id: 'p17', name: 'Naledi Mokwena', clubId: 'fochville', rank: 'A', isCoach: false },
    { id: 'p18', name: 'Grant Isaacs', clubId: 'fochville', rank: 'Unranked', isCoach: true },

    // Vaal Reefs
    { id: 'p19', name: 'Johan Pretorius', clubId: 'vaal-reefs', rank: 'B1', isCoach: false },
    { id: 'p20', name: 'Sipho Ndlovu', clubId: 'vaal-reefs', rank: 'Unranked', isCoach: true },
  ];

  private readonly coaches: CoachProfile[] = [
    {
      coachId: 'c1',
      player: this.players[3], // David Reddy - pcc
      bio: 'Former provincial player specialising in junior development and match strategy.',
      coachingLevel: 'SA Squash Level 2 Coach',
      hourlyRateZAR: 350,
      contactEmail: 'david.reddy@example.com',
    },
    {
      coachId: 'c2',
      player: this.players[5], // Riaan Coetzee - defence
      bio: 'Focuses on footwork drills and match-fitness conditioning for club-level players.',
      coachingLevel: 'SA Squash Level 1 Coach',
      hourlyRateZAR: 280,
      contactEmail: 'riaan.coetzee@example.com',
    },
    {
      coachId: 'c3',
      player: this.players[8], // Lindiwe Dlamini - klerksdorp
      bio: 'High-performance coach focused on fitness conditioning and advanced tactics.',
      coachingLevel: 'SA Squash Level 3 Coach',
      hourlyRateZAR: 450,
      contactEmail: 'lindiwe.dlamini@example.com',
    },
    {
      coachId: 'c4',
      player: this.players[11], // Pieter Human - rustenburg
      bio: 'Runs beginner and junior squad sessions, with a focus on technique fundamentals.',
      coachingLevel: 'SA Squash Level 2 Coach',
      hourlyRateZAR: 300,
      contactEmail: 'pieter.human@example.com',
    },
    {
      coachId: 'c5',
      player: this.players[14], // Anja Fourie - coligny
      bio: 'Volunteer club coach helping grow the sport in a small-town setting.',
      coachingLevel: 'SA Squash Level 1 Coach',
      hourlyRateZAR: 200,
      contactEmail: 'anja.fourie@example.com',
    },
    {
      coachId: 'c6',
      player: this.players[17], // Grant Isaacs - fochville
      bio: 'Helped restart the club after facility renovations; coaches both juniors and socials.',
      coachingLevel: 'SA Squash Level 1 Coach',
      hourlyRateZAR: 220,
      contactEmail: 'grant.isaacs@example.com',
    },
    {
      coachId: 'c7',
      player: this.players[19], // Sipho Ndlovu - vaal-reefs
      bio: 'Mining-community club coach with a focus on building a competitive social league.',
      coachingLevel: 'SA Squash Level 2 Coach',
      hourlyRateZAR: 320,
      contactEmail: 'sipho.ndlovu@example.com',
    },
  ];

  private readonly bookings: Booking[] = [
    {
      bookingId: 'bk1',
      hostPlayer: this.players[0], // Sarah Naidoo - pcc
      feeZAR: 100,
      status: 'Open',
      proposedLocation: 'Potchefstroom Country Club (PCC)',
      isCoachingSession: false,
      scheduledAt: '2026-08-15T09:00:00Z',
      createdAt: '2026-08-10T09:00:00Z',
    },
    {
      bookingId: 'bk2',
      hostPlayer: this.players[6], // Thabo Mokoena - klerksdorp
      opponent: this.players[1], // Kyle Botha - pcc
      feeZAR: 150,
      status: 'Accepted',
      proposedLocation: 'Klerksdorp Squash Club',
      isCoachingSession: false,
      scheduledAt: '2026-08-14T14:30:00Z',
      createdAt: '2026-08-08T14:30:00Z',
    },
    {
      bookingId: 'bk3',
      hostPlayer: this.players[10], // Karabo Mahlangu - rustenburg
      feeZAR: 120,
      status: 'Open',
      proposedLocation: 'Rustenburg Squash Club',
      isCoachingSession: false,
      scheduledAt: '2026-08-16T16:00:00Z',
      createdAt: '2026-08-11T16:00:00Z',
    },
    {
      bookingId: 'bk4',
      hostPlayer: this.players[15], // Liam Mostert - fochville
      opponent: this.players[12], // Andre Venter - coligny
      feeZAR: 80,
      status: 'Completed',
      proposedLocation: 'Fochville Squash Club',
      isCoachingSession: false,
      scheduledAt: '2026-08-02T10:00:00Z',
      createdAt: '2026-07-28T10:00:00Z',
    },
  ];

  private readonly clubs: SquashClub[] = [
    {
      clubId: 'pcc',
      name: 'Potchefstroom Country Club (PCC)',
      address: 'Govan Mbeki Avenue & Smit Street, Potchefstroom, 2531',
      coordinates: { lat: -26.7167, lng: 27.1 },
      players: [this.players[0], this.players[1], this.players[2], this.players[3]],
      coaches: [this.coaches[0]],
      activeBookings: [this.bookings[0]],
      phone: '018 294 6846',
      website: 'https://www.potchcc.co.za/',
    },
    {
      clubId: 'defence',
      name: 'Potchefstroom Defence Squash Club',
      address: 'Potchefstroom, North West',
      // Slightly offset from PCC so the two Potchefstroom markers don't sit
      // exactly on top of each other - no confirmed street address was found.
      coordinates: { lat: -26.722, lng: 27.093 },
      players: [this.players[4], this.players[5]],
      coaches: [this.coaches[1]],
      activeBookings: [],
      phone: '+27 83 669 2047',
      contactPerson: 'Henry Hornsby',
    },
    {
      clubId: 'klerksdorp',
      name: 'Klerksdorp Squash Club',
      address: 'Freemanville, Klerksdorp, 2571',
      coordinates: { lat: -26.8521, lng: 26.6667 },
      players: [this.players[6], this.players[7], this.players[8]],
      coaches: [this.coaches[2]],
      activeBookings: [this.bookings[1]],
      phone: '+27 83 630 3776',
      contactPerson: 'Bobby Stephenson',
    },
    {
      clubId: 'rustenburg',
      name: 'Rustenburg Squash Club',
      address: 'Rustenburg, North West',
      coordinates: { lat: -25.6676, lng: 27.2421 },
      players: [this.players[9], this.players[10], this.players[11]],
      coaches: [this.coaches[3]],
      activeBookings: [this.bookings[2]],
      phone: '014 594 0004',
      contactPerson: 'Tony Saayman',
    },
    {
      clubId: 'coligny',
      name: 'Coligny Squash Club',
      address: 'Coligny, North West',
      coordinates: { lat: -26.3333, lng: 26.3167 },
      players: [this.players[12], this.players[13], this.players[14]],
      coaches: [this.coaches[4]],
      activeBookings: [],
      phone: '+27 82 417 4827',
      contactPerson: 'Casper le Roux',
    },
    {
      clubId: 'fochville',
      name: 'Fochville Squash Club',
      address: 'Gert van Rensburg Stadium, Fochville',
      coordinates: { lat: -26.489, lng: 27.4903 },
      players: [this.players[15], this.players[16], this.players[17]],
      coaches: [this.coaches[5]],
      activeBookings: [this.bookings[3]],
      phone: '+27 72 193 8940',
      contactPerson: 'Adrian Stallenberg',
    },
    {
      clubId: 'vaal-reefs',
      name: 'Vaal Reefs',
      address: 'Vaal Reefs, Orkney area, North West',
      coordinates: { lat: -26.9802, lng: 26.6727 },
      players: [this.players[18], this.players[19]],
      coaches: [this.coaches[6]],
      activeBookings: [],
      phone: '+27 83 460 0810',
      contactPerson: 'Barry Worrall',
    },
  ];

  getClubs(): SquashClub[] {
    return this.clubs;
  }

  getPlayers(): Player[] {
    return this.players;
  }

  getCoaches(): CoachProfile[] {
    return this.coaches;
  }

  getActiveBookings(): Booking[] {
    return this.bookings;
  }
}
