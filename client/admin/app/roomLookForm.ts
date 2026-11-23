import { ChangeDetectionStrategy, Component, computed, input, output, signal } from "@angular/core";
import type { RoomLookData } from "../../src/looks";
import { type PainterPart, withField, withOpenLandscape, withoutGround, withPainter } from "../../src/lookForm";
import type { LookOptions } from "../../src/roomsData";
import { parseJson } from "../../src/roomsData";

/** A room's look as a form: colours, painters, wildlife; the backdrop's parts stay JSON for now. */
@Component({
  selector: "app-room-look-form",
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form">
      <div class="group">
        <strong>Colours</strong>
      <label>Sky <input type="color" [value]="look().sky" (input)="field('sky', $event)" /></label>
      <label>Floor colour <input type="color" [value]="look().floorColour" (input)="field('floorColour', $event)" /></label>
      <label>Wall colour <input type="color" [value]="look().wallColour" (input)="field('wallColour', $event)" /></label>
      </div>
      <label class="check">
        <input type="checkbox" [checked]="!!look().openLandscape" (change)="open($event)" />
        Open countryside (no back wall, a path through the north edge)
      </label>
      <label>
        Wildlife
        <select (change)="field('wildlife', $event)">
          <option value="" [selected]="!look().wildlife">None</option>
          @for (name of options().wildlife; track name) {
            <option [value]="name" [selected]="look().wildlife === name">{{ name }}</option>
          }
        </select>
      </label>
      @for (part of parts; track part.key) {
        <fieldset>
          <legend>{{ part.title }}</legend>
          <label>
            Painter
            <select [attr.aria-label]="part.title + ' painter'" (change)="painter(part.key, $event)">
              <option value="" [selected]="!painterOf(part.key)">{{ part.key === "ground" ? "Plain colour" : "None" }}</option>
              @for (name of part.names(options()); track name) {
                <option [value]="name" [selected]="painterOf(part.key) === name">{{ name }}</option>
              }
            </select>
          </label>
          @if (part.key === "ground" ? !!look().ground : !!painterOf(part.key)) {
            <label>Colour <input type="color" [attr.aria-label]="part.title + ' colour'" [value]="colourOf(part.key)" (input)="colour(part.key, 'colour', $event)" /></label>
            @if (part.key !== "ground") {
              <label>Accent <input type="color" [attr.aria-label]="part.title + ' accent'" [value]="accentOf(part.key)" (input)="colour(part.key, 'accent', $event)" /></label>
            }
          }
          @if (part.key === "ground" && look().ground) {
            <button type="button" (click)="noGround()">No ground</button>
          }
        </fieldset>
      }
      <details>
        <summary>Backdrop (JSON)</summary>
        <textarea rows="10" spellcheck="false" aria-label="Backdrop as JSON" [value]="backdropText()" (change)="backdrop($event)"></textarea>
        @if (backdropProblem()) {
          <p class="message" role="alert">{{ backdropProblem() }}</p>
        }
      </details>
    </div>
  `,
})
export class RoomLookForm {
  readonly look = input.required<RoomLookData>();
  readonly options = input.required<LookOptions>();
  readonly changed = output<RoomLookData>();
  protected readonly backdropProblem = signal("");
  protected readonly backdropText = computed(() => JSON.stringify(this.look().backdrop, null, 2));
  protected readonly parts: { key: PainterPart; title: string; names: (o: LookOptions) => string[] }[] = [
    { key: "floor", title: "Floor", names: (o) => o.floors },
    { key: "wall", title: "Back wall", names: (o) => o.walls },
    { key: "ground", title: "Ground round the floor", names: (o) => o.grounds },
  ];

  protected painterOf = (part: PainterPart): string => this.look()[part]?.painter ?? "";
  protected colourOf = (part: PainterPart): string => this.look()[part]?.colour ?? "#888888";
  protected accentOf = (part: PainterPart): string => (this.look()[part] as { accent?: string } | undefined)?.accent ?? "#888888";

  protected field(name: "sky" | "floorColour" | "wallColour" | "wildlife", event: Event): void {
    this.changed.emit(withField(this.look(), name, (event.target as HTMLInputElement).value));
  }

  protected open(event: Event): void {
    this.changed.emit(withOpenLandscape(this.look(), (event.target as HTMLInputElement).checked));
  }

  protected painter(part: PainterPart, event: Event): void {
    this.changed.emit(withPainter(this.look(), part, { painter: (event.target as HTMLSelectElement).value }));
  }

  protected colour(part: PainterPart, which: "colour" | "accent", event: Event): void {
    this.changed.emit(withPainter(this.look(), part, { [which]: (event.target as HTMLInputElement).value }));
  }

  protected noGround(): void {
    this.changed.emit(withoutGround(this.look()));
  }

  protected backdrop(event: Event): void {
    const parsed = parseJson((event.target as HTMLTextAreaElement).value) as RoomLookData["backdrop"] | null;
    if (parsed === null || !Array.isArray(parsed.parts)) {
      this.backdropProblem.set("The backdrop is JSON with a list of parts.");
      return;
    }
    this.backdropProblem.set("");
    this.changed.emit({ ...this.look(), backdrop: parsed });
  }
}
