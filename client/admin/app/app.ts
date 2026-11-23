import { ChangeDetectionStrategy, Component } from "@angular/core";
import { RouterLink, RouterOutlet } from "@angular/router";

@Component({
  selector: "app-root",
  imports: [RouterOutlet, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="bar">
      <a routerLink="/rooms" class="brand">Rooms</a>
      <a href="/admin">Moderation</a>
      <a href="/">Back to the game</a>
    </header>
    <main><router-outlet /></main>
  `,
})
export class App {}
