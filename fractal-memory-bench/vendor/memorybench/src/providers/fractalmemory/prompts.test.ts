import { describe, expect, test } from "bun:test"
import { FRACTALMEMORY_PROMPTS } from "./prompts"

describe("FractalMemory prompts", () => {
  test("answer prompt uses marked answer candidates and keeps context compact", () => {
    const answerPrompt = FRACTALMEMORY_PROMPTS.answerPrompt
    if (typeof answerPrompt !== "function") {
      throw new Error("Expected FractalMemory answerPrompt to be a function")
    }

    const prompt = answerPrompt(
      "When did Caroline go to the LGBTQ support group?",
      [
        {
          sessionId: "conv-26-session_19",
          answerCandidate: true,
          score: 0.51,
          state: "Caroline discussed adoption and family goals.",
        },
        {
          sessionId: "conv-26-session_1",
          answerCandidate: true,
          score: 0.92,
          state: "Caroline attended the LGBTQ support group.",
          scoreBreakdown: { matchedDates: ["2023-05-07"], matchedEventPhrases: ["support group"] },
        },
        {
          sessionId: "conv-26-session_7",
          answerCandidate: true,
          score: 0.43,
          state: "Caroline attended a neighborhood meeting.",
        },
        {
          sessionId: "conv-26-session_3",
          answerCandidate: false,
          score: 0.2,
          state: "Distractor session that should not be included.",
        },
      ],
      "2023-05-07"
    )

    expect(prompt).toBeDefined()
    expect(prompt).toContain("conv-26-session_1")
    expect(prompt).toContain("conv-26-session_19")
    expect(prompt).toContain("conv-26-session_7")
    expect(prompt).not.toContain("conv-26-session_3")
    expect(prompt?.match(/Result \d+/g)?.length).toBe(3)
  })
})
