/**
 * Unit tests for the Copilot Voice CLI extension.
 *
 * Uses Node.js built-in test runner (`node:test`).
 * Run with: node --test tests/extension/extension.test.mjs
 *
 * The extension.mjs is a single-file entry point that calls joinSession()
 * at module load. To test its pure helper functions without the SDK,
 * we set process.env.TESTING before importing so the module exports
 * its internals without calling joinSession().
 */

import { describe, it, beforeEach, afterEach, mock } from "node:test";
import assert from "node:assert/strict";

// Set TESTING env before importing the extension module
process.env.TESTING = "1";

const {
  truncateContent,
  buildMessagePayload,
  buildEventPayload,
  parseSSELines,
  postToCompanion,
  BASE_URL,
  MAX_PAYLOAD_BYTES,
  SSE_RECONNECT_MS,
  INITIAL_BACKOFF_MS,
  MAX_BACKOFF_MS,
} = await import(
  "../../.github/extensions/copilot-voice/extension.mjs"
);

// ---------------------------------------------------------------------------
// AC2/AC3: Payload builders — correct schema
// ---------------------------------------------------------------------------

describe("buildMessagePayload", () => {
  it("builds a valid message payload with required fields", () => {
    const payload = buildMessagePayload("Hello world");

    assert.equal(payload.type, "assistant.message");
    assert.equal(payload.content, "Hello world");
    assert.equal(payload.truncated, false);
    assert.equal(typeof payload.messageId, "string");
    assert.ok(payload.messageId.length > 0, "messageId must be non-empty");
    assert.equal(typeof payload.timestamp, "number");
    assert.ok(payload.timestamp > 0, "timestamp must be positive");
  });

  it("truncates content exceeding MAX_PAYLOAD_BYTES and sets truncated flag", () => {
    const longContent = "x".repeat(MAX_PAYLOAD_BYTES + 500);
    const payload = buildMessagePayload(longContent);

    assert.equal(payload.truncated, true);
    assert.ok(
      Buffer.byteLength(payload.content, "utf8") <= MAX_PAYLOAD_BYTES,
      `content should be <= ${MAX_PAYLOAD_BYTES} bytes`
    );
  });

  it("does not truncate content within limit", () => {
    const shortContent = "Hello";
    const payload = buildMessagePayload(shortContent);

    assert.equal(payload.truncated, false);
    assert.equal(payload.content, "Hello");
  });
});

describe("buildEventPayload", () => {
  it("builds a session.idle event payload", () => {
    const payload = buildEventPayload("session.idle", {});

    assert.equal(payload.type, "session.idle");
    assert.deepEqual(payload.data, {});
    assert.equal(typeof payload.timestamp, "number");
  });

  it("builds a tool.execution_start event with tool data", () => {
    const data = { toolName: "voice_speak" };
    const payload = buildEventPayload("tool.execution_start", data);

    assert.equal(payload.type, "tool.execution_start");
    assert.deepEqual(payload.data, { toolName: "voice_speak" });
  });

  it("builds a tool.execution_complete event with success data", () => {
    const data = { toolName: "voice_speak", success: true };
    const payload = buildEventPayload("tool.execution_complete", data);

    assert.equal(payload.type, "tool.execution_complete");
    assert.equal(payload.data.success, true);
  });
});

// ---------------------------------------------------------------------------
// Payload truncation — edge cases
// ---------------------------------------------------------------------------

describe("truncateContent", () => {
  it("returns content unchanged when under limit", () => {
    const result = truncateContent("short text", MAX_PAYLOAD_BYTES);
    assert.deepEqual(result, { content: "short text", truncated: false });
  });

  it("truncates content over limit", () => {
    const longText = "a".repeat(MAX_PAYLOAD_BYTES + 1000);
    const result = truncateContent(longText, MAX_PAYLOAD_BYTES);

    assert.equal(result.truncated, true);
    assert.ok(
      Buffer.byteLength(result.content, "utf8") <= MAX_PAYLOAD_BYTES,
      "truncated content must fit within limit"
    );
  });

  it("handles exactly at limit", () => {
    const exactText = "b".repeat(MAX_PAYLOAD_BYTES);
    const result = truncateContent(exactText, MAX_PAYLOAD_BYTES);

    assert.equal(result.truncated, false);
    assert.equal(result.content, exactText);
  });

  it("handles empty string", () => {
    const result = truncateContent("", MAX_PAYLOAD_BYTES);
    assert.deepEqual(result, { content: "", truncated: false });
  });

  it("handles multi-byte characters without breaking mid-character", () => {
    // Each emoji is 4 bytes in UTF-8. Create a string of emojis
    // that exceeds the limit and verify truncation doesn't break mid-char.
    const emoji = "🎤";
    const count = Math.ceil(MAX_PAYLOAD_BYTES / 4) + 10;
    const longEmoji = emoji.repeat(count);
    const result = truncateContent(longEmoji, MAX_PAYLOAD_BYTES);

    assert.equal(result.truncated, true);
    // Verify the truncated string is valid UTF-8 by round-tripping
    const encoded = Buffer.from(result.content, "utf8");
    const decoded = encoded.toString("utf8");
    assert.equal(decoded, result.content, "truncated content must be valid UTF-8");
  });
});

// ---------------------------------------------------------------------------
// AC4: SSE line parsing
// ---------------------------------------------------------------------------

describe("parseSSELines", () => {
  it("parses a complete SSE event with event and data lines", () => {
    const lines = [
      "event: send_prompt",
      'data: {"prompt": "hello", "attachments": []}',
      "",
    ];

    const events = parseSSELines(lines);

    assert.equal(events.length, 1);
    assert.equal(events[0].event, "send_prompt");
    assert.deepEqual(events[0].data, { prompt: "hello", attachments: [] });
  });

  it("parses multiple SSE events", () => {
    const lines = [
      "event: send_prompt",
      'data: {"prompt": "first"}',
      "",
      "event: send_prompt",
      'data: {"prompt": "second"}',
      "",
    ];

    const events = parseSSELines(lines);

    assert.equal(events.length, 2);
    assert.equal(events[0].data.prompt, "first");
    assert.equal(events[1].data.prompt, "second");
  });

  it("handles ping events", () => {
    const lines = ["event: ping", "data: {}", ""];

    const events = parseSSELines(lines);

    assert.equal(events.length, 1);
    assert.equal(events[0].event, "ping");
  });

  it("ignores comment lines (starting with colon)", () => {
    const lines = [
      ": this is a comment",
      "event: send_prompt",
      'data: {"prompt": "hello"}',
      "",
    ];

    const events = parseSSELines(lines);

    assert.equal(events.length, 1);
    assert.equal(events[0].event, "send_prompt");
  });

  it("handles data-only events (no event: line)", () => {
    const lines = ['data: {"prompt": "hello"}', ""];

    const events = parseSSELines(lines);

    assert.equal(events.length, 1);
    assert.equal(events[0].event, "message"); // default SSE event type
    assert.equal(events[0].data.prompt, "hello");
  });

  it("returns empty array for no events", () => {
    const events = parseSSELines([]);
    assert.deepEqual(events, []);
  });

  it("skips events with invalid JSON data", () => {
    const lines = ["event: send_prompt", "data: {invalid json", ""];

    const events = parseSSELines(lines);

    assert.equal(events.length, 0);
  });

  it("handles multi-line data (concatenated)", () => {
    const lines = [
      "event: send_prompt",
      'data: {"prompt":',
      'data:  "hello"}',
      "",
    ];

    const events = parseSSELines(lines);

    assert.equal(events.length, 1);
    assert.equal(events[0].data.prompt, "hello");
  });
});

// ---------------------------------------------------------------------------
// AC2: postToCompanion — HTTP POST logic
// ---------------------------------------------------------------------------

describe("postToCompanion", () => {
  let originalFetch;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it("sends POST request with correct URL, headers, and body", async () => {
    const calls = [];
    globalThis.fetch = mock.fn(async (url, options) => {
      calls.push({ url, options });
      return { ok: true, status: 200 };
    });

    const body = { type: "test", data: {} };
    await postToCompanion("/cli/event", body);

    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, `${BASE_URL}/cli/event`);
    assert.equal(calls[0].options.method, "POST");
    assert.equal(calls[0].options.headers["Content-Type"], "application/json");
    assert.deepEqual(JSON.parse(calls[0].options.body), body);
  });

  it("does not throw when fetch fails (fire-and-forget)", async () => {
    globalThis.fetch = mock.fn(async () => {
      throw new Error("ECONNREFUSED");
    });

    // Should not throw
    await assert.doesNotReject(async () => {
      await postToCompanion("/cli/event", { type: "test" });
    });
  });

  it("does not throw when response is not ok", async () => {
    globalThis.fetch = mock.fn(async () => ({
      ok: false,
      status: 500,
      statusText: "Internal Server Error",
    }));

    await assert.doesNotReject(async () => {
      await postToCompanion("/cli/message", { type: "test" });
    });
  });

  it("uses short timeout signal", async () => {
    globalThis.fetch = mock.fn(async (url, options) => {
      assert.ok(options.signal, "fetch should have an AbortSignal");
      return { ok: true, status: 200 };
    });

    await postToCompanion("/cli/event", { type: "test" });
  });
});

// ---------------------------------------------------------------------------
// AC5: Tool definitions
// ---------------------------------------------------------------------------

// We test that the tool definitions are exported with correct shapes.
// The actual handlers require the session object, so we test the tool metadata.

const { TOOL_DEFINITIONS } = await import(
  "../../.github/extensions/copilot-voice/extension.mjs"
);

describe("Tool definitions", () => {
  it("defines voice_speak tool with required properties", () => {
    const speakTool = TOOL_DEFINITIONS.find((t) => t.name === "voice_speak");
    assert.ok(speakTool, "voice_speak tool must be defined");
    assert.equal(typeof speakTool.description, "string");
    assert.ok(speakTool.description.length > 0);
    assert.ok(speakTool.parameters, "tool must have parameters");
    assert.ok(
      speakTool.parameters.properties.text,
      "voice_speak must accept text parameter"
    );
  });

  it("defines voice_set_avatar tool with required properties", () => {
    const avatarTool = TOOL_DEFINITIONS.find(
      (t) => t.name === "voice_set_avatar"
    );
    assert.ok(avatarTool, "voice_set_avatar tool must be defined");
    assert.equal(typeof avatarTool.description, "string");
    assert.ok(avatarTool.description.length > 0);
    assert.ok(avatarTool.parameters, "tool must have parameters");
    assert.ok(
      avatarTool.parameters.properties.expression,
      "voice_set_avatar must accept expression parameter"
    );
  });

  it("all tool names are prefixed with voice_ to avoid collisions", () => {
    for (const tool of TOOL_DEFINITIONS) {
      assert.ok(
        tool.name.startsWith("voice_"),
        `tool ${tool.name} must be prefixed with voice_`
      );
    }
  });
});

// ---------------------------------------------------------------------------
// AC6: Constants — verify design constraints
// ---------------------------------------------------------------------------

describe("Constants", () => {
  it("BASE_URL is hardcoded to localhost:7701", () => {
    assert.equal(BASE_URL, "http://localhost:7701");
  });

  it("MAX_PAYLOAD_BYTES is 10KB", () => {
    assert.equal(MAX_PAYLOAD_BYTES, 10 * 1024);
  });

  it("SSE_RECONNECT_MS is 5 seconds", () => {
    assert.equal(SSE_RECONNECT_MS, 5000);
  });

  it("INITIAL_BACKOFF_MS is 1 second", () => {
    assert.equal(INITIAL_BACKOFF_MS, 1000);
  });

  it("MAX_BACKOFF_MS is 30 seconds", () => {
    assert.equal(MAX_BACKOFF_MS, 30000);
  });
});

// ---------------------------------------------------------------------------
// AC5: Tool handler logic
// ---------------------------------------------------------------------------

const { createToolHandler } = await import(
  "../../.github/extensions/copilot-voice/extension.mjs"
);

describe("createToolHandler", () => {
  let originalFetch;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it("voice_speak handler POSTs text to /speak endpoint", async () => {
    const calls = [];
    globalThis.fetch = mock.fn(async (url, options) => {
      calls.push({ url, options });
      return { ok: true, status: 200 };
    });

    const handler = createToolHandler("/speak");
    const result = await handler({ text: "Hello world" });

    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, `${BASE_URL}/speak`);
    assert.deepEqual(JSON.parse(calls[0].options.body), {
      text: "Hello world",
    });
    assert.ok(result.includes("success"), "handler should return success message");
  });

  it("voice_set_avatar handler POSTs expression to /avatar endpoint", async () => {
    const calls = [];
    globalThis.fetch = mock.fn(async (url, options) => {
      calls.push({ url, options });
      return { ok: true, status: 200 };
    });

    const handler = createToolHandler("/avatar");
    const result = await handler({ expression: "thinking" });

    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, `${BASE_URL}/avatar`);
    assert.deepEqual(JSON.parse(calls[0].options.body), {
      expression: "thinking",
    });
  });

  it("handler returns error message when fetch fails", async () => {
    globalThis.fetch = mock.fn(async () => {
      throw new Error("ECONNREFUSED");
    });

    const handler = createToolHandler("/speak");
    const result = await handler({ text: "test" });

    assert.ok(
      result.toLowerCase().includes("error") ||
        result.toLowerCase().includes("unavailable"),
      "handler should indicate failure"
    );
  });
});

// ---------------------------------------------------------------------------
// Integration-style: event handler wiring
// ---------------------------------------------------------------------------

const { createEventHandlers } = await import(
  "../../.github/extensions/copilot-voice/extension.mjs"
);

describe("createEventHandlers", () => {
  let originalFetch;
  let fetchCalls;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
    fetchCalls = [];
    globalThis.fetch = mock.fn(async (url, options) => {
      fetchCalls.push({ url, options });
      return { ok: true, status: 200 };
    });
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it("assistant.message handler POSTs to /cli/message with correct schema", async () => {
    const handlers = createEventHandlers();

    await handlers["assistant.message"]({
      data: { content: "Hello from the agent" },
    });

    assert.equal(fetchCalls.length, 1);
    assert.ok(fetchCalls[0].url.endsWith("/cli/message"));
    const body = JSON.parse(fetchCalls[0].options.body);
    assert.equal(body.type, "assistant.message");
    assert.equal(body.content, "Hello from the agent");
    assert.equal(body.truncated, false);
  });

  it("session.idle handler POSTs to /cli/event", async () => {
    const handlers = createEventHandlers();

    await handlers["session.idle"]({ data: {} });

    assert.equal(fetchCalls.length, 1);
    assert.ok(fetchCalls[0].url.endsWith("/cli/event"));
    const body = JSON.parse(fetchCalls[0].options.body);
    assert.equal(body.type, "session.idle");
  });

  it("tool.execution_start handler POSTs to /cli/event with toolName", async () => {
    const handlers = createEventHandlers();

    await handlers["tool.execution_start"]({
      data: { toolName: "voice_speak" },
    });

    assert.equal(fetchCalls.length, 1);
    const body = JSON.parse(fetchCalls[0].options.body);
    assert.equal(body.type, "tool.execution_start");
    assert.equal(body.data.toolName, "voice_speak");
  });

  it("tool.execution_complete handler POSTs to /cli/event with success", async () => {
    const handlers = createEventHandlers();

    await handlers["tool.execution_complete"]({
      data: { toolName: "voice_speak", success: true },
    });

    assert.equal(fetchCalls.length, 1);
    const body = JSON.parse(fetchCalls[0].options.body);
    assert.equal(body.type, "tool.execution_complete");
    assert.equal(body.data.success, true);
  });
});

// ---------------------------------------------------------------------------
// AC4: SSE command dispatch
// ---------------------------------------------------------------------------

const { handleSSECommand } = await import(
  "../../.github/extensions/copilot-voice/extension.mjs"
);

describe("handleSSECommand", () => {
  it("dispatches send_prompt command to session.send()", () => {
    const sendCalls = [];
    const mockSession = {
      send: (payload) => sendCalls.push(payload),
      log: () => {},
    };

    handleSSECommand(
      mockSession,
      { event: "send_prompt", data: { prompt: "Hello agent", attachments: [] } }
    );

    assert.equal(sendCalls.length, 1);
    assert.equal(sendCalls[0].prompt, "Hello agent");
    assert.deepEqual(sendCalls[0].attachments, []);
  });

  it("ignores ping events", () => {
    const sendCalls = [];
    const mockSession = {
      send: (payload) => sendCalls.push(payload),
      log: () => {},
    };

    handleSSECommand(mockSession, { event: "ping", data: {} });

    assert.equal(sendCalls.length, 0);
  });

  it("rejects payloads exceeding 100KB", () => {
    const sendCalls = [];
    const logCalls = [];
    const mockSession = {
      send: (payload) => sendCalls.push(payload),
      log: (msg, opts) => logCalls.push({ msg, opts }),
    };

    const largePrompt = "x".repeat(101 * 1024);
    handleSSECommand(
      mockSession,
      { event: "send_prompt", data: { prompt: largePrompt } }
    );

    assert.equal(sendCalls.length, 0, "should not send oversized payload");
    assert.ok(logCalls.length > 0, "should log warning for oversized payload");
  });

  it("validates prompt is a string", () => {
    const sendCalls = [];
    const logCalls = [];
    const mockSession = {
      send: (payload) => sendCalls.push(payload),
      log: (msg, opts) => logCalls.push({ msg, opts }),
    };

    handleSSECommand(
      mockSession,
      { event: "send_prompt", data: { prompt: 12345 } }
    );

    assert.equal(sendCalls.length, 0, "should not send non-string prompt");
  });

  it("handles missing prompt gracefully", () => {
    const sendCalls = [];
    const logCalls = [];
    const mockSession = {
      send: (payload) => sendCalls.push(payload),
      log: (msg, opts) => logCalls.push({ msg, opts }),
    };

    handleSSECommand(
      mockSession,
      { event: "send_prompt", data: {} }
    );

    assert.equal(sendCalls.length, 0, "should not send empty prompt");
  });

  it("handles unknown event types gracefully", () => {
    const sendCalls = [];
    const mockSession = {
      send: (payload) => sendCalls.push(payload),
      log: () => {},
    };

    // Should not throw
    handleSSECommand(
      mockSession,
      { event: "unknown_event", data: { foo: "bar" } }
    );

    assert.equal(sendCalls.length, 0);
  });
});

// ---------------------------------------------------------------------------
// AC6/AC7: Backoff calculation
// ---------------------------------------------------------------------------

const { calculateBackoff } = await import(
  "../../.github/extensions/copilot-voice/extension.mjs"
);

describe("calculateBackoff", () => {
  it("returns initial backoff for attempt 0", () => {
    const delay = calculateBackoff(0);
    assert.equal(delay, INITIAL_BACKOFF_MS);
  });

  it("doubles backoff for each attempt", () => {
    assert.equal(calculateBackoff(1), INITIAL_BACKOFF_MS * 2);
    assert.equal(calculateBackoff(2), INITIAL_BACKOFF_MS * 4);
    assert.equal(calculateBackoff(3), INITIAL_BACKOFF_MS * 8);
  });

  it("caps backoff at MAX_BACKOFF_MS", () => {
    const delay = calculateBackoff(100);
    assert.equal(delay, MAX_BACKOFF_MS);
  });
});
