import type { PropView } from "./types";

const actions: Record<string, string> = {
  "map-board": "Read the town map",
  piano: "Play the café piano",
  "potting-bench": "Water the seedlings",
  "rod-rack": "Take a fishing rod",
  "pear-tree": "Pick a pear",
  "packing-crate": "Pack the pears",
  mirror: "Try on an outfit",
};

export function activityName(prop: PropView): string | undefined {
  return Object.hasOwn(actions, prop.kind) ? actions[prop.kind] : undefined;
}

/** The player's planting and harvest during this room visit. Neither activity spends or awards currency. */
export class RoomActivities {
  private picked = new Set<PropView>();
  private carrying = false;
  private packed = 0;
  private blossoms = 0;

  stage(prop: PropView): number {
    if (prop.kind === "pear-tree") return Number(this.picked.has(prop));
    if (prop.kind === "packing-crate") return this.packed;
    if (prop.kind === "potting-bench") return this.blossoms;
    return 0;
  }

  tend(prop: PropView, treeCount: number): string {
    if (prop.kind === "potting-bench") {
      this.blossoms = (this.blossoms + 1) % 4;
      switch (this.blossoms) {
        case 1: return "The first daisy opened.";
        case 2: return "Two daisies in bloom.";
        case 3: return "All three daisies are in bloom!";
        default: return "Fresh seedlings are ready to water.";
      }
    }
    if (prop.kind === "pear-tree") {
      if (this.carrying) return "Put your pear in the wooden crate first.";
      if (this.picked.has(prop)) return "This tree is picked. Try another tree.";
      this.picked.add(prop);
      this.carrying = true;
      return "One ripe pear! Carry it to the packing crate.";
    }
    if (prop.kind !== "packing-crate") throw new Error("Unknown gardening activity");
    if (this.packed === treeCount && treeCount > 0) {
      this.packed = 0;
      this.picked.clear();
      return "Crate shipped! A new harvest is ready.";
    }
    if (!this.carrying) return "Pick a golden pear for the crate.";
    this.carrying = false;
    this.packed++;
    return this.packed === treeCount ? "The crate is full! Tap it to ship the harvest." : `Pears packed: ${this.packed} of ${treeCount}.`;
  }
}

/** Play a short original phrase only when someone touches the piano. Release its audio device after the last note. */
let playing = false;
export async function playPiano(): Promise<void> {
  if (playing || typeof AudioContext === "undefined") return;
  const audio = new AudioContext();
  playing = true;
  try {
    await audio.resume();
    const melody = [261.63, 329.63, 392, 440, 392, 329.63];
    await new Promise<void>((done) => {
      melody.forEach((hz, i) => {
        const start = audio.currentTime + i * 0.16;
        const voice = audio.createOscillator();
        const gain = audio.createGain();
        voice.type = "triangle";
        voice.frequency.value = hz;
        gain.gain.setValueAtTime(0.001, start);
        gain.gain.exponentialRampToValueAtTime(0.1, start + 0.012);
        gain.gain.exponentialRampToValueAtTime(0.001, start + 0.3);
        voice.connect(gain).connect(audio.destination);
        if (i === melody.length - 1) voice.onended = () => done();
        voice.start(start);
        voice.stop(start + 0.31);
      });
    });
  } finally {
    playing = false;
    await audio.close();
  }
}
