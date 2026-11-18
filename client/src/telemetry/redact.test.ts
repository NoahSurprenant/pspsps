import { describe, expect, it } from "vitest";
import { REDACTED, redactUrl, redactUrlsIn } from "./redact";

describe("redactUrl", () => {
  it("keeps the path and harmless query values, and drops the origin and fragment", () => {
    expect(redactUrl("https://game.example/api/me?page=2#top")).toBe("/api/me?page=2");
  });

  it("redacts sensitive query values, whatever their case", () => {
    expect(redactUrl("/auth/callback?code=abc&state=xyz&Access_Token=t&page=1"))
      .toBe(`/auth/callback?code=${REDACTED}&state=${REDACTED}&Access_Token=${REDACTED}&page=1`);
    expect(redactUrl("/x?userId=42&email=a%40b.c")).toBe(`/x?userId=${REDACTED}&email=${REDACTED}`);
  });

  it("redacts SignalR's connection token, and only a parameter named just id", () => {
    expect(redactUrl("/hubs/game?id=secret-token")).toBe(`/hubs/game?id=${REDACTED}`);
    expect(redactUrl("/x?width=3")).toBe("/x?width=3");
  });

  it("gives nothing for nothing", () => {
    expect(redactUrl(undefined)).toBe("");
    expect(redactUrl("")).toBe("");
  });
});

describe("redactUrlsIn", () => {
  it("redacts the query strings in a message or stack, wherever they are", () => {
    expect(redactUrlsIn("WebSocket failed to connect to 'wss://game.example/hubs/game?id=abc'. Try again"))
      .toBe(`WebSocket failed to connect to 'wss://game.example/hubs/game?id=${REDACTED}'. Try again`);
    expect(redactUrlsIn("see /auth/callback?code=abc&page=2 then")).toBe(`see /auth/callback?code=${REDACTED}&page=2 then`);
    expect(redactUrlsIn("at load (https://game.example/assets/index.js?token=x:1:2)"))
      .toBe(`at load (https://game.example/assets/index.js?token=${REDACTED})`);
  });

  it("leaves text without a query string alone", () => {
    expect(redactUrlsIn("Is this a question? Yes")).toBe("Is this a question? Yes");
  });
});
