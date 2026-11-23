import { ChangeDetectionStrategy, Component, inject, signal } from "@angular/core";
import { RouterLink } from "@angular/router";
import type { VariantData } from "../../src/looks";
import { blankVariant, newPainterTemplate, newWildlifeTemplate, recipeBody, recipeFrom, variantBody, withVariantFloor } from "../../src/libraryData";
import type { LookOptions } from "../../src/roomsData";
import { messageOf } from "./http";
import { RoomsApi } from "./roomsApi";

type Kind = "painters" | "wildlife";

/** What rooms share: seasonal look variants (for overlays), and the painters and creatures made as data. */
@Component({
  selector: "app-library",
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/rooms">&larr; All rooms</a></p>
    <h1>Looks library</h1>
    @if (message()) {
      <p class="message" role="status">{{ message() }}</p>
    }
    <section class="card">
      <h2>Variants</h2>
      <p class="quiet">A seasonal look an overlay can put on a room: sky and wall colours, and a floor of its own (a frozen pond).</p>
      @for (variant of variants(); track variant.id) {
        <fieldset>
          <legend>{{ variant.id }}</legend>
          <label>Sky <input type="color" [attr.aria-label]="variant.id + ' sky'" [value]="variant.sky" (input)="setColour(variant, 'sky', $event)" /></label>
          <label>Wall <input type="color" [attr.aria-label]="variant.id + ' wall'" [value]="variant.wallColour" (input)="setColour(variant, 'wallColour', $event)" /></label>
          <label>
            Own floor
            <select [attr.aria-label]="variant.id + ' floor painter'" (change)="floor(variant, 'painter', $event)">
              <option value="" [selected]="!variant.floor">None</option>
              @for (name of options()?.floors ?? []; track name) {
                <option [value]="name" [selected]="variant.floor?.painter === name">{{ name }}</option>
              }
            </select>
          </label>
          @if (variant.floor) {
            <label>Colour <input type="color" [attr.aria-label]="variant.id + ' floor colour'" [value]="variant.floor.colour ?? '#888888'" (input)="floor(variant, 'colour', $event)" /></label>
            <label>Accent <input type="color" [attr.aria-label]="variant.id + ' floor accent'" [value]="variant.floor.accent ?? '#888888'" (input)="floor(variant, 'accent', $event)" /></label>
          }
          <div class="actions">
            <button type="button" class="primary" [disabled]="busy()" (click)="saveVariant(variant)">Save {{ variant.id }}</button>
            <button type="button" [disabled]="busy()" (click)="deleteVariant(variant.id)">Delete</button>
          </div>
        </fieldset>
      } @empty {
        <p class="quiet">No variants yet.</p>
      }
      <div class="actions">
        <input aria-label="New variant name" placeholder="winter" [value]="newVariant()" (input)="newVariant.set(text($event))" />
        <button type="button" [disabled]="busy() || !newVariant().trim()" (click)="addVariant()">Add variant</button>
      </div>
    </section>
    @for (section of recipeSections; track section.kind) {
      <section class="card">
        <h2>{{ section.title }}</h2>
        <p class="quiet">{{ section.hint }}</p>
        <ul class="rooms">
          @for (entry of entries(section.kind); track entry['id']) {
            <li class="actions">
              <span>{{ entry['id'] }}</span>
              <button type="button" (click)="edit(section.kind, entry)">Edit</button>
              <button type="button" [disabled]="busy()" (click)="deleteRecipe(section.kind, str(entry['id']))">Delete</button>
            </li>
          } @empty {
            <li class="quiet">None yet.</li>
          }
        </ul>
        @if (editing() === section.kind) {
          <textarea rows="14" spellcheck="false" [attr.aria-label]="section.title + ' recipe as JSON'" [value]="draft()" (input)="draft.set(text($event))"></textarea>
          <div class="actions">
            <button type="button" class="primary" [disabled]="busy()" (click)="saveRecipe(section.kind)">Save</button>
            <button type="button" (click)="editing.set(null)">Cancel</button>
          </div>
        } @else {
          <button type="button" (click)="start(section.kind)">New {{ section.single }}</button>
        }
      </section>
    }
  `,
})
export class LibraryPage {
  private readonly api = inject(RoomsApi);
  protected readonly variants = signal<VariantData[]>([]);
  protected readonly painters = signal<Record<string, unknown>[]>([]);
  protected readonly creatures = signal<Record<string, unknown>[]>([]);
  protected readonly options = signal<LookOptions | null>(null);
  protected readonly message = signal("");
  protected readonly busy = signal(false);
  protected readonly newVariant = signal("");
  protected readonly editing = signal<Kind | null>(null);
  protected readonly draft = signal("");
  protected readonly recipeSections: { kind: Kind; title: string; single: string; hint: string }[] = [
    { kind: "painters", title: "Painters", single: "painter", hint: "Named floor and wall paintings a look can use: a surface and a list of steps." },
    { kind: "wildlife", title: "Wildlife", single: "creature", hint: "Creatures that live in a room: parts and how they move." },
  ];

  constructor() {
    void this.load();
    void this.api.options().then((options) => this.options.set(options), () => undefined);
  }

  protected entries = (kind: Kind): Record<string, unknown>[] => (kind === "painters" ? this.painters() : this.creatures());
  protected str = (value: unknown): string => String(value);
  protected text = (event: Event): string => (event.target as HTMLInputElement | HTMLTextAreaElement).value;

  protected setColour(variant: VariantData, field: "sky" | "wallColour", event: Event): void {
    this.replace({ ...variant, [field]: this.text(event) });
  }

  protected floor(variant: VariantData, field: "painter" | "colour" | "accent", event: Event): void {
    this.replace(withVariantFloor(variant, { [field]: this.text(event) }));
  }

  protected addVariant(): void {
    const id = this.newVariant().trim();
    if (this.variants().some((variant) => variant.id === id)) {
      this.message.set(`There is already a variant ${id}.`);
      return;
    }
    this.variants.update((all) => [...all, blankVariant(id)]);
    this.newVariant.set("");
    this.message.set(`${id} is added here: save it to keep it.`);
  }

  protected saveVariant(variant: VariantData): Promise<void> {
    return this.act(async () => {
      await this.api.saveVariant(variant.id, variantBody(variant));
      await this.load();
      return `${variant.id} is saved.`;
    });
  }

  protected deleteVariant(id: string): Promise<void> {
    return this.act(async () => {
      await this.api.deleteVariant(id);
      await this.load();
      return `${id} is gone.`;
    });
  }

  protected start(kind: Kind): void {
    this.draft.set(JSON.stringify(kind === "painters" ? newPainterTemplate() : newWildlifeTemplate(), null, 2));
    this.editing.set(kind);
  }

  protected edit(kind: Kind, entry: Record<string, unknown>): void {
    this.draft.set(JSON.stringify(entry, null, 2));
    this.editing.set(kind);
  }

  protected saveRecipe(kind: Kind): Promise<void> {
    return this.act(async () => {
      const made = recipeFrom(this.draft(), kind);
      if ("problem" in made) return made.problem;
      await this.api.saveRecipe(kind, String(made.entry["id"]), recipeBody(kind, made.entry));
      this.editing.set(null);
      await this.load();
      return `${String(made.entry["id"])} is saved.`;
    });
  }

  protected deleteRecipe(kind: Kind, id: string): Promise<void> {
    return this.act(async () => {
      await this.api.deleteRecipe(kind, id);
      await this.load();
      return `${id} is gone.`;
    });
  }

  private replace(changed: VariantData): void {
    this.variants.update((all) => all.map((variant) => (variant.id === changed.id ? changed : variant)));
  }

  private async act(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    try {
      this.message.set(await action());
    } catch (error) {
      this.message.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const library = await this.api.looks();
      this.variants.set(library.variants);
      this.painters.set(library.painters ?? []);
      this.creatures.set(library.wildlife ?? []);
    } catch (error) {
      this.message.set(messageOf(error));
    }
  }
}
