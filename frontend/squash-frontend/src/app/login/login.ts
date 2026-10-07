import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Auth } from '../services/auth';
import { MockData } from '../services/mock-data';
import { SquashClub } from '../models/squash-domain';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  // Clubs are still static reference data (see mock-data.ts) - registration
  // just needs a list of clubs for a new player to say which one they belong to.
  protected readonly clubs: SquashClub[];

  protected readonly mode = signal<'login' | 'register'>('login');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly isSubmitting = signal(false);

  protected email = '';
  protected password = '';
  protected name = '';
  protected surname = '';
  protected clubId = '';
  protected isCoach = false;

  constructor(
    private auth: Auth,
    private mockData: MockData
  ) {
    // Deliberately set here rather than as a field initializer - field
    // initializers run before constructor-injected properties are assigned,
    // so this.mockData wouldn't be available yet up there.
    this.clubs = this.mockData.getClubs();
    this.clubId = this.clubs[0]?.clubId ?? '';
  }

  protected toggleMode(): void {
    this.mode.set(this.mode() === 'login' ? 'register' : 'login');
    this.errorMessage.set(null);
  }

  protected submit(): void {
    this.errorMessage.set(null);
    this.isSubmitting.set(true);

    const request$ =
      this.mode() === 'login'
        ? this.auth.login({ email: this.email, password: this.password })
        : this.auth.register({
            name: this.name,
            surname: this.surname,
            email: this.email,
            password: this.password,
            clubId: this.clubId,
            isCoach: this.isCoach,
          });

    request$.subscribe({
      next: () => {
        this.isSubmitting.set(false);
        // No navigation needed - App reacts to auth.currentPlayer() directly.
      },
      error: (err) => {
        this.isSubmitting.set(false);
        if (err.status === 401) {
          this.errorMessage.set('Invalid email or password.');
        } else if (err.status === 409) {
          this.errorMessage.set('An account with this email already exists.');
        } else {
          this.errorMessage.set('Something went wrong - is the backend running?');
        }
      },
    });
  }
}
