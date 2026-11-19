import { describe, expect, it } from "vitest";
import {
  ErrorReporter, HUB_MESSAGE_LEFT_OUT, MAX_MESSAGE, MAX_REPORTS, NO_MESSAGE, describeError, isRefusal, type ReporterHost,
} from "./errors";
import { REDACTED } from "./redact";
import { telemetryOn } from "./testing";

function reporter(host: Partial<ReporterHost> = {}, on = true) {
  const { telemetry, host: sink } = telemetryOn(on ? 1 : "off");
  const errors = new ErrorReporter(telemetry, {
    href: () => "https://game.example/?code=abc",
    cancelledByBrowser: () => false,
    ...host,
  });
  const logs = () => {
    telemetry.flush();
    return sink.sent.flatMap((batch) => batch.body.logs);
  };
  return { errors, logs, sink };
}

describe("describeError", () => {
  it("names the error, and keeps its stack", () => {
    const error = new TypeError("x is undefined");
    expect(describeError(error)).toEqual({ message: "TypeError: x is undefined", stack: error.stack });
  });

  it("leaves out what the server said in a refused hub call: it may name a player", () => {
    const error = new Error("An unexpected error occurred invoking 'GoToFriend' on the server. HubException: Felix isn't in a room right now.");
    expect(isRefusal(error)).toBe(true);
    expect(describeError(error).message).toBe(
      `Error: An unexpected error occurred invoking 'GoToFriend' on the server. HubException: ${HUB_MESSAGE_LEFT_OUT}`);
    expect(describeError(error).stack).not.toContain("Felix");
  });

  it("redacts query values and cuts to size", () => {
    expect(describeError(new Error("failed /auth/callback?code=abc")).message).toBe(`Error: failed /auth/callback?code=${REDACTED}`);
    expect(describeError("x".repeat(5000)).message).toHaveLength(MAX_MESSAGE);
  });

  it("describes whatever was thrown", () => {
    expect(describeError("plain").message).toBe("plain");
    expect(describeError({ a: 1 }).message).toBe('{"a":1}');
    expect(describeError("").message).toBe(NO_MESSAGE);
    expect(describeError(new Error("")).message).toBe(`Error: ${NO_MESSAGE}`);
  });
});

describe("ErrorReporter", () => {
  it("reports an error with its kind, stack and the page, redacted", () => {
    const { errors, logs } = reporter();
    errors.reportError(new Error("boom"), "uncaught");
    const [line] = logs();
    expect(line).toMatchObject({ level: "error", message: "Error: boom" });
    expect(line?.attributes).toMatchObject({ "error.kind": "uncaught", page: `/?code=${REDACTED}` });
    expect(line?.attributes["exception.stacktrace"]).toContain("boom");
  });

  it("reports each error once, and at most MAX_REPORTS a page", () => {
    const { errors, logs } = reporter();
    errors.reportError(new Error("same"), "uncaught");
    errors.reportError(new Error("same"), "uncaught");
    for (let i = 0; i < MAX_REPORTS + 5; i++) {
      errors.reportError(new Error(`error ${i}`), "uncaught");
    }
    const lines = logs();
    expect(lines).toHaveLength(MAX_REPORTS);
    expect(lines.filter((line) => line.message === "Error: same")).toHaveLength(1);
  });

  it("reports calls with no answer or a 5xx, in their trace, and not a 4xx", () => {
    const { errors, logs } = reporter();
    const trace = { traceId: "a".repeat(32), spanId: "b".repeat(16) };
    errors.reportCall("GET", "/api/me?token=t", 503, trace);
    errors.reportCall("POST", "/auth/guest", 0, undefined, new TypeError("Failed to fetch"));
    errors.reportCall("GET", "/api/me", 404);
    const [server, none, ...rest] = logs();
    expect(rest).toEqual([]);
    expect(server).toMatchObject({ message: `GET /api/me?token=${REDACTED} answered 503`, traceId: trace.traceId, spanId: trace.spanId });
    expect(server?.attributes).toMatchObject({ "error.kind": "http", "http.response.status_code": 503 });
    expect(none?.message).toBe("POST /auth/guest got no answer: TypeError: Failed to fetch");
  });

  it("doesn't report a call the browser cut off (leaving, hidden, offline)", () => {
    const { errors, logs } = reporter({ cancelledByBrowser: () => true });
    errors.reportCall("GET", "/api/me", 0);
    expect(logs()).toEqual([]);
  });

  it("sends at once what it reports as the page goes", () => {
    const { errors, sink } = reporter({ cancelledByBrowser: () => true });
    errors.reportError(new Error("late"), "uncaught");
    expect(sink.fetch).toHaveBeenCalledOnce();
  });

  it("reports uncaught errors and unhandled rejections once installed", () => {
    const { errors, logs } = reporter();
    const target = new EventTarget();
    errors.install(target as unknown as Window);
    target.dispatchEvent(Object.assign(new Event("error"), { error: new Error("uncaught one"), message: "Uncaught Error: uncaught one" }));
    target.dispatchEvent(Object.assign(new Event("unhandledrejection"), { reason: new Error("rejected one") }));
    expect(logs().map((line) => [line.message, line.attributes["error.kind"]])).toEqual([
      ["Error: uncaught one", "uncaught"],
      ["Error: rejected one", "unhandledrejection"],
    ]);
  });

  it("does nothing when telemetry is off", () => {
    const { errors, sink } = reporter({}, false);
    const target = new EventTarget();
    errors.install(target as unknown as Window);
    target.dispatchEvent(Object.assign(new Event("error"), { error: new Error("x") }));
    errors.reportCall("GET", "/api/me", 500);
    errors.reportError(new Error("y"), "uncaught");
    expect(sink.fetch).not.toHaveBeenCalled();
  });
});
