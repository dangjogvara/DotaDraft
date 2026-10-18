import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { HeroApiService } from './heroes/hero-api.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly heroApi = inject(HeroApiService);

  // Temporary connectivity check until the hero grid (#41) replaces it
  protected readonly heroCount = signal<number | null>(null);
  protected readonly loadFailed = signal(false);

  constructor() {
    this.heroApi.getHeroes().subscribe({
      next: (heroes) => this.heroCount.set(heroes.length),
      error: () => this.loadFailed.set(true),
    });
  }
}
