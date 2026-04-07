/**
 * Copilot Voice — CLI Extension Bridge
 *
 * Bridges the Copilot Voice companion app and the Copilot CLI session.
 * Forwards agent responses and session events to the companion app via HTTP,
 * receives voice commands via SSE, and registers native voice tools.
 *
 * Part of the v2 rearchitecture (epic #59).
 *
 * @see https://github.com/vbomfim/copilot-voice/issues/60
 *
 * Design constraints:
 * - Single file, zero npm dependencies
 * - stdout reserved for JSON-RPC — use session.log() only
 * - Hardcoded localhost:7701 — no configuration via untrusted input
 * - Max 10KB payload for message content (truncated with flag)
 * - Graceful degradation when companion app unreachable
 */

import crypto from "node:crypto";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Base URL for the companion app HTTP server. Hardcoded for security. */
export const BASE_URL = "http://localhost:7701";

/** Maximum payload size in bytes for message content (10KB). */
export const MAX_PAYLOAD_BYTES = 10 * 1024;

/** Delay before reconnecting a dropped SSE connection (5 seconds). */
export const SSE_RECONNECT_MS = 5000;

/** Initial backoff delay for companion app connection retry (1 second). */
export const INITIAL_BACKOFF_MS = 1000;

/** Maximum backoff delay for companion app connection retry (30 seconds). */
export const MAX_BACKOFF_MS = 30000;

/** Maximum inbound payload size from companion app (100KB). */
const MAX_INBOUND_BYTES = 100 * 1024;

/** Maximum SSE buffer size before forced reconnect (256KB). */
const MAX_SSE_BUFFER_BYTES = 256 * 1024;

/** Maximum connection attempts before giving up (20 ≈ ~5 min). */
export const MAX_CONNECT_ATTEMPTS = 20;

/** HTTP request timeout in milliseconds (5 seconds). */
const HTTP_TIMEOUT_MS = 5000;

// ---------------------------------------------------------------------------
// Helpers — pure functions
// ---------------------------------------------------------------------------

/**
 * Truncate content to fit within a byte limit.
 * Handles multi-byte characters safely by encoding to a Buffer.
 *
 * @param {string} content - The content to potentially truncate.
 * @param {number} maxBytes - Maximum allowed byte length.
 * @returns {{ content: string, truncated: boolean }}
 */
export function truncateContent(content, maxBytes) {
  const byteLength = Buffer.byteLength(content, "utf8");

  if (byteLength <= maxBytes) {
    return { content, truncated: false };
  }

  // Encode to Buffer and slice to maxBytes, then decode back.
  // Buffer.toString("utf8") handles incomplete multi-byte chars safely.
  const buffer = Buffer.from(content, "utf8");
  const truncated = buffer.subarray(0, maxBytes).toString("utf8");

  // Remove the last character if it was a replacement character (broken multi-byte)
  const cleaned = truncated.endsWith("\uFFFD")
    ? truncated.slice(0, -1)
    : truncated;

  return { content: cleaned, truncated: true };
}

/**
 * Build a message payload for forwarding assistant responses.
 *
 * @param {string} content - The assistant message content.
 * @returns {object} Message payload matching the outbound schema.
 */
export function buildMessagePayload(content) {
  const { content: finalContent, truncated } = truncateContent(
    content,
    MAX_PAYLOAD_BYTES,
  );

  return {
    type: "assistant.message",
    content: finalContent,
    messageId: crypto.randomUUID(),
    timestamp: Date.now(),
    truncated,
  };
}

/**
 * Build an event payload for forwarding session events.
 *
 * @param {string} type - Event type (session.idle, tool.execution_start, etc.).
 * @param {object} data - Event data.
 * @returns {object} Event payload matching the outbound schema.
 */
export function buildEventPayload(type, data) {
  return {
    type,
    data: data ?? {},
    timestamp: Date.now(),
  };
}

/**
 * Calculate exponential backoff delay with a maximum cap.
 *
 * @param {number} attempt - Zero-based attempt number.
 * @returns {number} Delay in milliseconds.
 */
export function calculateBackoff(attempt) {
  const delay = INITIAL_BACKOFF_MS * Math.pow(2, attempt);
  return Math.min(delay, MAX_BACKOFF_MS);
}

// ---------------------------------------------------------------------------
// HTTP — fire-and-forget POST to companion app
// ---------------------------------------------------------------------------

/**
 * Send a POST request to the companion app. Errors are swallowed
 * to avoid blocking the CLI session — this is fire-and-forget.
 *
 * @param {string} path - URL path (e.g., "/cli/message").
 * @param {object} body - JSON-serializable request body.
 * @param {function} [logFn] - Optional logging function.
 */
export async function postToCompanion(path, body, logFn) {
  try {
    await fetch(`${BASE_URL}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(HTTP_TIMEOUT_MS),
    });
  } catch {
    // Fire-and-forget: companion may be unreachable. Caller handles logging.
    if (logFn) {
      logFn(`Failed to POST ${path}`, { level: "warning" });
    }
  }
}

// ---------------------------------------------------------------------------
// SSE parsing
// ---------------------------------------------------------------------------

/**
 * Parse an array of SSE text lines into structured events.
 * Follows the SSE specification: events are separated by blank lines,
 * "event:" sets the event type, "data:" carries the payload.
 *
 * @param {string[]} lines - Array of raw text lines from the SSE stream.
 * @returns {Array<{ event: string, data: object }>} Parsed events.
 */
export function parseSSELines(lines) {
  const events = [];
  let currentEvent = "message";
  let currentData = "";

  for (const line of lines) {
    // Blank line = event boundary
    if (line === "") {
      if (currentData) {
        try {
          const data = JSON.parse(currentData);
          events.push({ event: currentEvent, data });
        } catch {
          // Invalid JSON — skip this event
        }
      }
      currentEvent = "message";
      currentData = "";
      continue;
    }

    // Comment lines start with colon
    if (line.startsWith(":")) {
      continue;
    }

    if (line.startsWith("event:")) {
      currentEvent = line.slice(6).trim();
    } else if (line.startsWith("data:")) {
      const dataValue = line.slice(5).trim();
      currentData += currentData ? "\n" + dataValue : dataValue;
    } else if (line.startsWith("id:") || line.startsWith("retry:")) {
      // SSE spec fields — acknowledged but not used
    }
  }

  return events;
}

// ---------------------------------------------------------------------------
// SSE command handling
// ---------------------------------------------------------------------------

/**
 * Handle a parsed SSE command by dispatching to the appropriate session action.
 *
 * @param {object} session - The Copilot CLI session object.
 * @param {{ event: string, data: object }} command - Parsed SSE command.
 */
export function handleSSECommand(session, command) {
  const { event, data } = command;

  if (event === "ping") {
    return;
  }

  if (event === "send_prompt") {
    // Validate prompt
    if (!data || typeof data.prompt !== "string" || data.prompt.length === 0) {
      session.log("Received send_prompt with invalid or missing prompt", {
        level: "warning",
      });
      return;
    }

    // Reject oversized payloads (100KB limit)
    if (Buffer.byteLength(JSON.stringify(data), "utf8") > MAX_INBOUND_BYTES) {
      session.log("Rejected oversized command payload (>100KB)", {
        level: "warning",
      });
      return;
    }

    const attachments = Array.isArray(data.attachments) ? data.attachments : [];

    session.send({
      prompt: data.prompt,
      attachments,
    });
    return;
  }

  // Unknown event type — log and ignore (sanitize for log safety)
  const safeEvent = String(event).replace(/[\x00-\x1f]/g, "").slice(0, 50);
  session.log(`Received unknown SSE event: ${safeEvent}`, { level: "info" });
}

// ---------------------------------------------------------------------------
// Tool definitions
// ---------------------------------------------------------------------------

/**
 * Create a tool handler that POSTs the tool arguments to a companion endpoint.
 *
 * @param {string} path - Companion app endpoint path (e.g., "/speak").
 * @returns {function} Async tool handler function.
 */
export function createToolHandler(path) {
  return async (args) => {
    try {
      const response = await fetch(`${BASE_URL}${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(args),
        signal: AbortSignal.timeout(HTTP_TIMEOUT_MS),
      });
      if (!response.ok) {
        return `Voice companion returned error: HTTP ${response.status}`;
      }
      return `Voice command sent successfully to ${path}`;
    } catch {
      return `Voice companion unavailable — could not reach ${BASE_URL}${path}`;
    }
  };
}

/** Tool definitions registered with the CLI session. */
export const TOOL_DEFINITIONS = [
  {
    name: "voice_speak",
    description:
      "Speak text aloud through the Copilot Voice companion app. " +
      "Use for status updates, questions, and summaries. " +
      "Keep messages short (1-2 sentences).",
    parameters: {
      type: "object",
      properties: {
        text: {
          type: "string",
          description: "The text to speak aloud via TTS.",
        },
      },
      required: ["text"],
    },
    handler: createToolHandler("/speak"),
  },
  {
    name: "voice_set_avatar",
    description:
      "Change the avatar expression in the Copilot Voice companion app. " +
      "Available expressions: normal, thinking, speaking, listening, " +
      "focused, relaxed, sleeping.",
    parameters: {
      type: "object",
      properties: {
        expression: {
          type: "string",
          description:
            "Avatar expression to set. One of: normal, thinking, speaking, " +
            "listening, focused, relaxed, sleeping.",
          enum: [
            "normal",
            "thinking",
            "speaking",
            "listening",
            "focused",
            "relaxed",
            "sleeping",
          ],
        },
      },
      required: ["expression"],
    },
    handler: createToolHandler("/avatar"),
  },
];

// ---------------------------------------------------------------------------
// Event handlers
// ---------------------------------------------------------------------------

/**
 * Create event handler functions for CLI session events.
 * Each handler forwards the event to the companion app via HTTP POST.
 *
 * @returns {object} Map of event names to handler functions.
 */
export function createEventHandlers() {
  return {
    "assistant.message": async (event) => {
      const content = event?.data?.content ?? "";
      const payload = buildMessagePayload(content);
      await postToCompanion("/cli/message", payload);
    },

    "session.idle": async (event) => {
      const payload = buildEventPayload("session.idle", event?.data ?? {});
      await postToCompanion("/cli/event", payload);
    },

    "tool.execution_start": async (event) => {
      const payload = buildEventPayload(
        "tool.execution_start",
        event?.data ?? {},
      );
      await postToCompanion("/cli/event", payload);
    },

    "tool.execution_complete": async (event) => {
      const payload = buildEventPayload(
        "tool.execution_complete",
        event?.data ?? {},
      );
      await postToCompanion("/cli/event", payload);
    },
  };
}

// ---------------------------------------------------------------------------
// SSE connection — streaming fetch to companion app
// ---------------------------------------------------------------------------

/**
 * Connect to the companion app SSE endpoint and process incoming commands.
 * Automatically reconnects on disconnect with a fixed delay.
 *
 * @param {object} session - The Copilot CLI session object.
 */
async function connectSSE(session) {
  let running = true;

  const connect = async () => {
    while (running) {
      try {
        session.log("Connecting to companion app SSE stream...", {
          level: "info",
        });

        const response = await fetch(`${BASE_URL}/cli/commands`, {
          headers: { Accept: "text/event-stream" },
        });

        if (!response.ok) {
          session.log(
            `SSE connection failed: HTTP ${response.status}`,
            { level: "warning" },
          );
          await delay(SSE_RECONNECT_MS);
          continue;
        }

        session.log("Connected to companion app SSE stream", {
          level: "info",
        });

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";

        while (true) {
          const { done, value } = await reader.read();

          if (done) {
            // Flush any remaining buffered data before reconnecting
            if (buffer.trim()) {
              const lines = buffer.split("\n");
              lines.push(""); // add blank line to flush final event
              const events = parseSSELines(lines);
              for (const event of events) {
                handleSSECommand(session, event);
              }
              buffer = "";
            }
            session.log("SSE stream ended, reconnecting...", {
              level: "info",
            });
            break;
          }

          buffer += decoder.decode(value, { stream: true });

          // Guard against unbounded buffer growth (256KB cap)
          if (Buffer.byteLength(buffer, "utf8") > MAX_SSE_BUFFER_BYTES) {
            session.log(
              "SSE buffer exceeded 256KB limit, discarding and reconnecting",
              { level: "warning" },
            );
            buffer = "";
            break;
          }

          // Process complete lines (SSE events are line-delimited)
          const lineEnd = buffer.lastIndexOf("\n");
          if (lineEnd === -1) continue;

          const completeChunk = buffer.slice(0, lineEnd);
          buffer = buffer.slice(lineEnd + 1);

          const lines = completeChunk.split("\n");
          const events = parseSSELines(lines);

          for (const event of events) {
            handleSSECommand(session, event);
          }
        }
      } catch (error) {
        const message = error?.message ?? "unknown error";
        session.log(`SSE connection error: ${message}`, { level: "warning" });
      }

      // Reconnect after delay
      if (running) {
        await delay(SSE_RECONNECT_MS);
      }
    }
  };

  // Run in background — don't block session startup
  connect();

  return () => {
    running = false;
  };
}

/**
 * Connect to the companion app with exponential backoff.
 * Sends an initial health check to verify the companion is reachable.
 *
 * @param {object} session - The Copilot CLI session object.
 */
async function connectWithBackoff(session) {
  let attempt = 0;

  while (attempt < MAX_CONNECT_ATTEMPTS) {
    try {
      const response = await fetch(`${BASE_URL}/health`, {
        signal: AbortSignal.timeout(HTTP_TIMEOUT_MS),
      });

      if (response.ok) {
        session.log("Companion app is reachable", { level: "info" });
        return;
      }
    } catch {
      // Connection failed — retry with backoff
    }

    const backoffMs = calculateBackoff(attempt);
    session.log(
      `Companion app unreachable, retrying in ${backoffMs / 1000}s... (attempt ${attempt + 1}/${MAX_CONNECT_ATTEMPTS})`,
      { level: "warning" },
    );
    await delay(backoffMs);
    attempt++;
  }

  session.log(
    "Companion app unreachable after maximum retries. Voice features disabled. Restart the companion app and run /clear to retry.",
    { level: "warning" },
  );
}

// ---------------------------------------------------------------------------
// Utility
// ---------------------------------------------------------------------------

/**
 * Promise-based delay.
 *
 * @param {number} ms - Milliseconds to wait.
 * @returns {Promise<void>}
 */
function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// ---------------------------------------------------------------------------
// Main — Extension entry point
// ---------------------------------------------------------------------------

/**
 * Initialize the Copilot Voice CLI extension.
 * Called at module load unless TESTING env is set.
 */
async function main() {
  // Dynamic import — only available in the CLI runtime
  const { joinSession } = await import("@github/copilot-sdk/extension");

  const eventHandlers = createEventHandlers();

  const session = await joinSession({
    tools: TOOL_DEFINITIONS,
  });

  session.log("Copilot Voice extension loaded", { level: "info" });

  // Register event listeners
  for (const [eventName, handler] of Object.entries(eventHandlers)) {
    session.on(eventName, handler);
  }

  // Connect to companion app (backoff loop runs in background)
  // DISABLED for debugging — SSE connection not needed for push-to-talk testing
  // connectWithBackoff(session)
  //   .then(() => {
  //     connectSSE(session);
  //   })
  //   .catch((err) => {
  //     session.log(`Connection failed: ${err?.message ?? "unknown error"}`, {
  //       level: "error",
  //     });
  //   });
  session.log("SSE connection disabled for debugging", { level: "warning" });
}

// Guard: skip main() during testing so we can import helpers
if (!process.env.TESTING) {
  main();
}
