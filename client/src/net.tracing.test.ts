import { describe, expect, it } from "vitest";
import netSource from "./net.ts?raw";

// Every hub call goes through GameConnection.call, which traces it (its traceparent in the call's headers) and logs
// refusals and failures without their arguments. A call made on the hub directly would be neither.
describe("GameConnection's hub calls", () => {
  it("all go through call(): the hub is invoked in one place", () => {
    const direct = [...netSource.matchAll(/\bhub\s*\.\s*(invoke|send|stream)\s*[<(]/g)];
    expect(direct, "hub.invoke/send/stream outside call()").toHaveLength(1);

    const call = /private call<T = void>\(method: string, \.\.\.args: unknown\[\]\): Promise<T> \{\n\s*return hubTracer\.invoke\(method, \(\) => this\.hub\.invoke<T>\(method, \.\.\.args\)\);\n\s*\}/
      .exec(netSource);
    expect(call, "call() traces through hubTracer").not.toBeNull();
    expect(direct[0]?.index).toBeGreaterThan(call?.index ?? Infinity);
    expect(direct[0]?.index).toBeLessThan((call?.index ?? 0) + (call?.[0].length ?? 0));
  });

  it("include the moderation calls", () => {
    for (const method of ["Moderate", "GetWarnings", "SawWarning"]) {
      expect(netSource).toMatch(new RegExp(`this\\.call(<[^>]+>)?\\(\\s*"${method}"`));
    }
  });
});
