import type { ProviderPrompts } from "../../types/prompts"

interface FractalMemoryResult {
  sessionId?: string
  title?: string
  score?: number
  answerCandidate?: boolean
  matchedFiles?: string[]
  scoreBreakdown?: {
    matchedDates?: string[]
    matchedEventPhrases?: string[]
    matchedEntities?: string[]
  }
  index?: string
  state?: string
  decisions?: string
  timeline_excerpt?: string
}

function trimSection(content: string | undefined, maxChars: number): string {
  if (!content) return ""
  const compact = content.replace(/\n{3,}/g, "\n\n").trim()
  if (compact.length <= maxChars) return compact
  return `${compact.slice(0, maxChars).trim()}...`
}

function resolveAnswerTopK(): number {
  const raw = Number.parseInt(process.env.FRACTALMEMORY_ANSWER_TOP_K || "3", 10)
  return Number.isNaN(raw) ? 3 : Math.max(1, raw)
}

function buildFractalContext(context: unknown[]): string {
  const typed = context as FractalMemoryResult[]
  const flagged = typed.filter((result) => result.answerCandidate)
  const results = (flagged.length > 0 ? flagged : typed).slice(0, resolveAnswerTopK())
  if (results.length === 0) {
    return "No search results were retrieved."
  }

  return results
    .map((result, index) => {
      const parts = [
        `Result ${index + 1} | Session: ${result.sessionId || result.title || "unknown"} | Score: ${typeof result.score === "number" ? result.score.toFixed(3) : "n/a"}`,
      ]

      if (result.matchedFiles && result.matchedFiles.length > 0) {
        parts.push(`Matched Files: ${result.matchedFiles.join(", ")}`)
      }
      if (result.scoreBreakdown?.matchedEventPhrases?.length) {
        parts.push(`Matched Event Phrases: ${result.scoreBreakdown.matchedEventPhrases.join(", ")}`)
      }
      if (result.scoreBreakdown?.matchedDates?.length) {
        parts.push(`Matched Dates: ${result.scoreBreakdown.matchedDates.join(", ")}`)
      }

      const state = trimSection(result.state, 420)
      const decisions = trimSection(result.decisions, 520)
      const timeline = trimSection(result.timeline_excerpt, 520)
      const indexSummary = trimSection(result.index, 320)

      if (state) parts.push(`State:\n${state}`)
      if (decisions) parts.push(`Decisions:\n${decisions}`)
      if (timeline) parts.push(`Timeline Excerpt:\n${timeline}`)
      if (indexSummary) parts.push(`Index Summary:\n${indexSummary}`)

      return parts.join("\n")
    })
    .join("\n\n---\n\n")
}

export const FRACTALMEMORY_PROMPTS: ProviderPrompts = {
  answerPrompt: (question: string, context: unknown[], questionDate?: string) => {
    const contextText = buildFractalContext(context)
    return `You are answering a long-memory benchmark question using a Fractal Memory style retrieval packet.

Question: ${question}
Question Date: ${questionDate || "Not specified"}

Retrieved Fractal Memory Context:
${contextText}

Instructions:
- Use only the retrieved context.
- Prefer the session state and decisions over long timeline details when they conflict.
- Prefer exact dates and directly stated events over broader thematic similarity.
- If the retrieved context is insufficient, answer with "I don't know".
- Keep the answer concise and factual.
- Return only the answer sentence, with no reasoning.

Answer:`
  },
}
