import { HttpTransportType, HubConnectionState, type HubConnection, type IHttpConnectionOptions } from "@microsoft/signalr";
import { describe, expect, it, vi } from "vitest";
import { GameConnection, hubTransports, type GameEvents } from "./net";

/** The part of a HubConnection GameConnection uses: it records its handlers, and start does what the test says. */
class FakeHub {
  state = HubConnectionState.Disconnected;
  readonly closeHandlers: ((error?: Error) => void)[] = [];
  readonly reconnectingHandlers: ((error?: Error) => void)[] = [];

  constructor(readonly options: IHttpConnectionOptions, private readonly opens: boolean) {}

  start(): Promise<void> {
    if (!this.opens) return Promise.reject(new Error("WebSocket failed to connect."));
    this.state = HubConnectionState.Connected;
    return Promise.resolve();
  }

  stop(): Promise<void> {
    this.state = HubConnectionState.Disconnected;
    return Promise.resolve();
  }

  on(): void {}
  onreconnected(): void {}
  onreconnecting(handler: (error?: Error) => void): void {
    this.reconnectingHandlers.push(handler);
  }
  onclose(handler: (error?: Error) => void): void {
    this.closeHandlers.push(handler);
  }
}

function events(): GameEvents {
  return new Proxy({}, { get: (target: Record<string, unknown>, name: string) => (target[name] ??= vi.fn()) }) as unknown as GameEvents;
}

/** The nth hub made. */
function hub(hubs: FakeHub[], n: number): FakeHub {
  const made = hubs[n];
  if (!made) throw new Error(`no hub ${n}`);
  return made;
}

/** A GameConnection on fake hubs, the nth of which opens when `opens[n]`. */
function connection(opens: boolean[], on = events()) {
  const hubs: FakeHub[] = [];
  const game = new GameConnection(on, (options) => {
    const hub = new FakeHub(options, opens[hubs.length] ?? false);
    hubs.push(hub);
    return hub as unknown as HubConnection;
  });
  return { game, hubs, on };
}

describe("the hub's transport", () => {
  it("is a WebSocket straight away, with no negotiate request: it stays on the pod it opened on", async () => {
    const { game, hubs } = connection([true]);

    await game.start();

    expect(hubs).toHaveLength(1);
    expect(hub(hubs, 0).options).toEqual({ transport: HttpTransportType.WebSockets, skipNegotiation: true });
  });

  it("falls back once to SignalR's negotiation, with every transport, when the WebSocket can't open", async () => {
    const { game, hubs } = connection([false, true]);

    await game.start();

    expect(hubs.map((hub) => hub.options)).toEqual([...hubTransports]);
    expect(hub(hubs, 1).options).toEqual({});
    expect(hub(hubs, 1).state).toBe(HubConnectionState.Connected);
  });

  it("fails when neither opens (a banned player is refused both ways)", async () => {
    const { game, hubs } = connection([false, false]);

    await expect(game.start()).rejects.toThrow("WebSocket failed to connect.");
    expect(hubs).toHaveLength(2);
  });

  it("doesn't let the connection it gave up on tell the page it closed", async () => {
    const { game, hubs, on } = connection([false, true]);
    await game.start();

    hub(hubs, 0).closeHandlers.forEach((handler) => handler(new Error("gone")));
    hub(hubs, 0).reconnectingHandlers.forEach((handler) => handler(new Error("gone")));
    expect(on.closed).not.toHaveBeenCalled();
    expect(on.reconnecting).not.toHaveBeenCalled();

    hub(hubs, 1).closeHandlers.forEach((handler) => handler(new Error("gone")));
    expect(on.closed).toHaveBeenCalledOnce();
  });

  it("tries the WebSocket first again on the next start: one failure (a pod restarting, say) doesn't stick", async () => {
    const { game, hubs } = connection([false, true, true]);
    await game.start();
    hub(hubs, 1).state = HubConnectionState.Disconnected;

    await game.start();

    expect(hubs).toHaveLength(3);
    expect(hub(hubs, 2).options).toEqual({ transport: HttpTransportType.WebSockets, skipNegotiation: true });
    expect(hub(hubs, 2).state).toBe(HubConnectionState.Connected);
  });

  it("isn't connected again while it is up", async () => {
    const { game, hubs } = connection([true]);
    await game.start();

    await game.start();

    expect(hubs).toHaveLength(1);
  });
});
