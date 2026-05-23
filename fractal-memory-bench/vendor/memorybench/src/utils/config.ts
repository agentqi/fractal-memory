export interface Config {
  supermemoryApiKey: string
  supermemoryBaseUrl: string
  mem0ApiKey: string
  zepApiKey: string
  openaiApiKey: string
  anthropicApiKey: string
  googleApiKey: string
}

export const config: Config = {
  supermemoryApiKey: process.env.SUPERMEMORY_API_KEY || "",
  supermemoryBaseUrl: process.env.SUPERMEMORY_BASE_URL || "https://api.supermemory.ai",
  mem0ApiKey: process.env.MEM0_API_KEY || "",
  zepApiKey: process.env.ZEP_API_KEY || "",
  openaiApiKey: process.env.OPENAI_API_KEY || "",
  anthropicApiKey: process.env.ANTHROPIC_API_KEY || "",
  googleApiKey: process.env.GOOGLE_API_KEY || "",
}

function parseNumberEnv(name: string, fallback: number): number {
  const raw = process.env[name]
  if (!raw) return fallback
  const parsed = Number.parseFloat(raw)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parseBooleanEnv(name: string, fallback: boolean): boolean {
  const raw = process.env[name]
  if (!raw) return fallback
  if (["1", "true", "yes", "on"].includes(raw.toLowerCase())) return true
  if (["0", "false", "no", "off"].includes(raw.toLowerCase())) return false
  return fallback
}

export function getProviderConfig(
  provider: string
): { apiKey: string; baseUrl?: string; [key: string]: unknown } {
  switch (provider) {
    case "supermemory":
      return { apiKey: config.supermemoryApiKey, baseUrl: config.supermemoryBaseUrl }
    case "mem0":
      return { apiKey: config.mem0ApiKey }
    case "zep":
      return { apiKey: config.zepApiKey }
    case "filesystem":
      return { apiKey: config.openaiApiKey } // Filesystem uses OpenAI for memory extraction
    case "rag":
      return { apiKey: config.openaiApiKey } // RAG provider uses OpenAI for embeddings
    case "fractalmemory":
      return {
        apiKey: config.openaiApiKey,
        retrievalTopK: parseNumberEnv("FRACTALMEMORY_RETRIEVAL_TOP_K", 10),
        answerTopK: parseNumberEnv("FRACTALMEMORY_ANSWER_TOP_K", 3),
        enableRankingDebug: parseBooleanEnv("FRACTALMEMORY_ENABLE_RANKING_DEBUG", true),
        rankingDebugTopN: parseNumberEnv("FRACTALMEMORY_RANKING_DEBUG_TOP_N", 5),
      }
    default:
      throw new Error(`Unknown provider: ${provider}`)
  }
}

export function getJudgeConfig(judge: string): { apiKey: string; model?: string } {
  switch (judge) {
    case "openai":
      return { apiKey: config.openaiApiKey }
    case "anthropic":
      return { apiKey: config.anthropicApiKey }
    case "google":
      return { apiKey: config.googleApiKey }
    default:
      throw new Error(`Unknown judge: ${judge}`)
  }
}
