import { mkdtemp, readFile, readdir } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { afterEach, describe, expect, test } from "bun:test"
import type { UnifiedSession } from "../../types/unified"
import { FractalMemoryProvider } from "./index"

const originalCwd = process.cwd()

afterEach(() => {
  process.chdir(originalCwd)
})

function makeSession(
  sessionId: string,
  date: string,
  content: string,
  extracted?: string
): UnifiedSession {
  return {
    sessionId,
    metadata: {
      formattedDate: date,
      speakerA: "Caroline",
      speakerB: "Facilitator",
      extracted,
    },
    messages: [
      {
        role: "user",
        speaker: "Caroline",
        timestamp: "2023-05-07T10:00:00Z",
        content,
      },
    ],
  }
}

async function withProvider(
  root: string,
  extractionCalls: { count: number },
  fn: (provider: FractalMemoryProvider) => Promise<void>,
  options?: { extractionPipelineVersion?: string; benchmark?: string; answerTopK?: number; retrievalTopK?: number }
) {
  process.chdir(root)
  const provider = new FractalMemoryProvider(async (_openai, session) => {
    extractionCalls.count += 1
    const extracted = session.metadata?.extracted
    if (typeof extracted === "string") return extracted
    return ["## Key Facts", `- ${session.messages[0]?.content ?? ""}`].join("\n")
  })
  await provider.initialize({
    apiKey: "test-key",
    benchmark: options?.benchmark ?? "locomo",
    extractionPipelineVersion: options?.extractionPipelineVersion,
    answerTopK: options?.answerTopK,
    retrievalTopK: options?.retrievalTopK,
  })
  try {
    await fn(provider)
  } finally {
    process.chdir(originalCwd)
  }
}

describe("FractalMemoryProvider cache", () => {
  test("first run creates cache manifest and second unchanged run reuses it", async () => {
    const extractionCalls = { count: 0 }
    let firstSearch: unknown[] = []
    const root = await mkdtemp(join(tmpdir(), "fractalmemory-provider-"))

    await withProvider(root, extractionCalls, async (provider) => {
      const session = makeSession(
        "conv-26-session_1",
        "7 May 2023",
        "Caroline went to the LGBTQ support group on 7 May 2023.",
        [
          "## Key Facts",
          "- Caroline went to the LGBTQ support group on 7 May 2023.",
          "",
          "## Events",
          "- [7 May 2023]: Caroline attended an LGBTQ support group.",
          "",
          "## Decisions & Plans",
          "- Bring discussion notes to the next session.",
        ].join("\n")
      )
      await provider.ingest([session], { containerTag: "container-a" })
      firstSearch = await provider.search("When did Caroline go to the LGBTQ support group?", {
        containerTag: "container-a",
        limit: 3,
      })

      const cacheRoot = join(root, "data", "cache", "fractalmemory")
      const cacheEntries = await readdir(cacheRoot)
      expect(cacheEntries.length).toBe(1)

      const cacheManifest = JSON.parse(
        await readFile(join(cacheRoot, cacheEntries[0]!, "manifest.json"), "utf-8")
      ) as Record<string, unknown>
      expect(cacheManifest.benchmark).toBe("locomo")
      expect(cacheManifest.extractionPipelineVersion).toBe("1")

      const containerManifest = JSON.parse(
        await readFile(
          join(root, "data", "providers", "fractalmemory", "container-a", "manifest.json"),
          "utf-8"
        )
      ) as Record<string, unknown>
      expect((containerManifest.sessions as unknown[]).length).toBe(1)
      expect(
        ((containerManifest.metrics as Record<string, unknown>).extractionSeconds as number) >= 0
      ).toBe(true)
    })

    await withProvider(root, extractionCalls, async (provider) => {
      const session = makeSession(
        "conv-26-session_1",
        "7 May 2023",
        "Caroline went to the LGBTQ support group on 7 May 2023.",
        [
          "## Key Facts",
          "- Caroline went to the LGBTQ support group on 7 May 2023.",
          "",
          "## Events",
          "- [7 May 2023]: Caroline attended an LGBTQ support group.",
          "",
          "## Decisions & Plans",
          "- Bring discussion notes to the next session.",
        ].join("\n")
      )
      await provider.ingest([session], { containerTag: "container-b" })
      const secondSearch = await provider.search("When did Caroline go to the LGBTQ support group?", {
        containerTag: "container-b",
        limit: 3,
      })
      expect(secondSearch).toEqual(firstSearch)
    })

    expect(extractionCalls.count).toBe(1)
  })

  test("changed source invalidates cache", async () => {
    const extractionCalls = { count: 0 }
    const root = await mkdtemp(join(tmpdir(), "fractalmemory-provider-"))

    await withProvider(root, extractionCalls, async (provider) => {
      await provider.ingest([makeSession("conv-26-session_1", "7 May 2023", "Caroline went to the LGBTQ support group on 7 May 2023.")], {
        containerTag: "container-a",
      })
    })

    await withProvider(root, extractionCalls, async (provider) => {
      await provider.ingest([makeSession("conv-26-session_1", "18 July 2023", "Caroline went to the LGBTQ support group on 18 July 2023.")], {
        containerTag: "container-b",
      })
    })

    expect(extractionCalls.count).toBe(2)
  })

  test("pipeline version change invalidates cache", async () => {
    const extractionCalls = { count: 0 }
    const root = await mkdtemp(join(tmpdir(), "fractalmemory-provider-"))

    await withProvider(root, extractionCalls, async (provider) => {
      await provider.ingest([makeSession("conv-26-session_1", "7 May 2023", "Caroline went to the LGBTQ support group on 7 May 2023.")], {
        containerTag: "container-a",
      })
    })

    await withProvider(
      root,
      extractionCalls,
      async (provider) => {
        await provider.ingest([makeSession("conv-26-session_1", "7 May 2023", "Caroline went to the LGBTQ support group on 7 May 2023.")], {
          containerTag: "container-b",
        })
      },
      { extractionPipelineVersion: "2" }
    )

    expect(extractionCalls.count).toBe(2)
  })

  test("temporal event session outranks broad thematic session and writes ranking debug", async () => {
    const extractionCalls = { count: 0 }
    const root = await mkdtemp(join(tmpdir(), "fractalmemory-provider-"))

    await withProvider(root, extractionCalls, async (provider) => {
      await provider.ingest(
        [
          makeSession(
            "conv-26-session_1",
            "7 May 2023",
            "Caroline attended the LGBTQ support group on 7 May 2023.",
            [
              "## Key Facts",
              "- Caroline attended the LGBTQ support group.",
              "",
              "## Events",
              "- [7 May 2023]: Caroline attended the LGBTQ support group.",
            ].join("\n")
          ),
          makeSession(
            "conv-26-session_19",
            "22 October 2023",
            "Caroline discussed adoption and family goals in October 2023.",
            [
              "## Key Facts",
              "- Caroline passed the adoption agency interviews on 20 October 2023.",
              "",
              "## Decisions & Plans",
              "- Caroline wants to build a family through adoption.",
            ].join("\n")
          ),
          makeSession(
            "conv-26-session_7",
            "16 June 2023",
            "Caroline attended a neighborhood planning meeting.",
            [
              "## Events",
              "- [16 June 2023]: Caroline attended a neighborhood planning meeting.",
            ].join("\n")
          ),
        ],
        { containerTag: "conv-26-q0-run-123" }
      )

      const results = (await provider.search("When did Caroline go to the LGBTQ support group?", {
        containerTag: "conv-26-q0-run-123",
        limit: 10,
      })) as Array<Record<string, unknown>>

      expect(results[0]?.sessionId).toBe("conv-26-session_1")
      expect(results.slice(0, 3).some((item) => item.sessionId === "conv-26-session_1")).toBe(true)
      expect(results[0]?.answerCandidate).toBe(true)
      expect(results[1]?.score).toBeLessThan(results[0]?.score as number)

      const debugPath = join(root, "data", "runs", "run-123", "ranking_debug", "conv-26-q0.json")
      const debugPayload = JSON.parse(await readFile(debugPath, "utf-8")) as {
        candidates: Array<{ sessionId: string; breakdown: { matchedEventPhrases: string[]; dateScore: number } }>
      }
      expect(debugPayload.candidates[0]?.sessionId).toBe("conv-26-session_1")
      expect(debugPayload.candidates[0]?.breakdown.matchedEventPhrases).toContain("support group")
      expect(debugPayload.candidates[0]?.breakdown.dateScore).toBeGreaterThan(0)
    })
  })

  test("search remains deterministic across repeated calls", async () => {
    const extractionCalls = { count: 0 }
    const root = await mkdtemp(join(tmpdir(), "fractalmemory-provider-"))

    await withProvider(root, extractionCalls, async (provider) => {
      await provider.ingest(
        [
          makeSession("conv-26-session_1", "7 May 2023", "Caroline attended the LGBTQ support group."),
          makeSession("conv-26-session_19", "22 October 2023", "Caroline discussed adoption and family goals."),
        ],
        { containerTag: "conv-26-q0-run-456" }
      )

      const first = (await provider.search("When did Caroline go to the LGBTQ support group?", {
        containerTag: "conv-26-q0-run-456",
        limit: 10,
      })) as Array<Record<string, unknown>>
      const second = (await provider.search("When did Caroline go to the LGBTQ support group?", {
        containerTag: "conv-26-q0-run-456",
        limit: 10,
      })) as Array<Record<string, unknown>>

      expect(first.map((item) => item.sessionId)).toEqual(second.map((item) => item.sessionId))
    })
  })
})
