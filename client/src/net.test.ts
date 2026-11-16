import { describe, expect, it } from "vitest";
import { messageOf } from "./net";

describe("messageOf", () => {
  it("keeps only the server's message from a hub error", () => {
    const error = new Error("An unexpected error occurred invoking 'Say' on the server. HubException: Say something.");

    expect(messageOf(error)).toBe("Say something.");
  });

  it("passes other errors through", () => {
    expect(messageOf(new Error("Network down"))).toBe("Network down");
    expect(messageOf("odd")).toBe("odd");
  });
});
