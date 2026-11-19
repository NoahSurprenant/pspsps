import { describe, expect, it, vi } from "vitest";
import { AppLog, MAX_LOGS } from "./appLog";
import { telemetryOn } from "./testing";

function log(on = true) {
  const { telemetry, host } = telemetryOn(on ? 1 : "off");
  const output = { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
  const lines = () => {
    telemetry.flush();
    return host.sent.flatMap((batch) => batch.body.logs);
  };
  return { appLog: new AppLog(telemetry, output), output, lines };
}

describe("AppLog", () => {
  it("writes to the console and sends info and above, with the cause's details", () => {
    const { appLog, output, lines } = log();
    const cause = new Error("dropped");
    appLog.warn("The hub connection dropped; reconnecting", cause, { attempt: 1 });
    expect(output.warn).toHaveBeenCalledWith("The hub connection dropped; reconnecting", cause);
    expect(lines()[0]).toMatchObject({
      level: "warn",
      message: "The hub connection dropped; reconnecting",
      attributes: { attempt: 1, "exception.message": "Error: dropped" },
    });
  });

  it("keeps debug to the console", () => {
    const { appLog, output, lines } = log();
    appLog.debug("detail");
    expect(output.debug).toHaveBeenCalledWith("detail");
    expect(lines()).toEqual([]);
  });

  it("sends each line once a page", () => {
    const { appLog, output, lines } = log();
    appLog.info("The hub connection is up");
    appLog.info("The hub connection is up");
    appLog.info("A hub call was refused", undefined, { "rpc.method": "Move" });
    appLog.info("A hub call was refused", undefined, { "rpc.method": "Say" });
    expect(output.info).toHaveBeenCalledTimes(4);
    expect(lines()).toHaveLength(3);
  });

  it("sends at most MAX_LOGS of each level, so info can't crowd out errors", () => {
    const { appLog, lines } = log();
    for (let i = 0; i < MAX_LOGS + 10; i++) {
      appLog.info(`info ${i}`);
    }
    appLog.error("still sent");
    const sent = lines();
    expect(sent.filter((line) => line.level === "info")).toHaveLength(MAX_LOGS);
    expect(sent.filter((line) => line.level === "error")).toHaveLength(1);
  });

  it("sends a line without writing it to the console (what Babylon wrote already)", () => {
    const { appLog, output, lines } = log();
    appLog.send("error", "Babylon: error", "Unable to compile effect");
    expect(output.error).not.toHaveBeenCalled();
    expect(lines()[0]).toMatchObject({ message: "Babylon: error", attributes: { "error.detail": "Unable to compile effect" } });
  });

  it("only writes to the console when telemetry is off", () => {
    const { appLog, output, lines } = log(false);
    appLog.error("The 3D scene didn't load", new Error("chunk"));
    expect(output.error).toHaveBeenCalledOnce();
    expect(lines()).toEqual([]);
  });
});
