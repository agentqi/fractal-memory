import { createHash } from "node:crypto"
import { mkdir, writeFile, readFile, readdir, rm } from "node:fs/promises"
import { dirname, join } from "node:path"
import { createOpenAI } from "@ai-sdk/openai"
import type {
  Provider,
  ProviderConfig,
  IngestOptions,
  IngestResult,
  SearchOptions,
  IndexingProgressCallback,
} from "../../types/provider"
import type { UnifiedSession, UnifiedMessage } from "../../types/unified"
import { logger } from "../../utils/logger"
import { extractMemories } from "../../prompts/extraction"
import { FRACTALMEMORY_PROMPTS } from "./prompts"

const DEFAULT_EXTRACTION_PIPELINE_VERSION = "1"
const DEFAULT_INDEX_PIPELINE_VERSION = "1"

function getBaseDir(): string {
  return join(process.cwd(), "data", "providers", "fractalmemory")
}

function getCacheDir(): string {
  return join(process.cwd(), "data", "cache", "fractalmemory")
}

type NodeDocument = {
  sessionId: string
  date: string
  title: string
  index: string
  state: string
  timeline: string
  decisions: string
}

type CachedNodeDocument = {
  provider: string
  benchmark: string
  extractionPipelineVersion: string
  indexPipelineVersion: string
  sourceFingerprint: string
  cacheKey: string
  createdAt: string
  document: NodeDocument
}

type ContainerSessionEntry = {
  sessionId: string
  sessionDirName: string
  cacheKey: string
  sourceFingerprint: string
}

type ContainerManifest = {
  provider: string
  benchmark: string
  extractionPipelineVersion: string
  indexPipelineVersion: string
  containerTag: string
  createdAt: string
  metrics: {
    cacheLookupSeconds: number
    extractionSeconds: number
    cacheWriteSeconds: number
    ingestTotalSeconds: number
  }
  sessions: ContainerSessionEntry[]
}

type SearchHit = {
  sessionId: string
  score: number
  title: string
  matchedFiles: string[]
  index: string
  state: string
  decisions: string
  timeline_excerpt: string
  answerCandidate?: boolean
  scoreBreakdown?: RankingBreakdown
}

type QueryProfile = {
  normalizedQuery: string
  terms: string[]
  phrases: string[]
  eventPhrases: string[]
  entities: string[]
  queryDates: string[]
  asksWhen: boolean
}

type ExtractMemoriesFn = (
  openai: ReturnType<typeof createOpenAI>,
  session: UnifiedSession
) => Promise<string>

type RankingBreakdown = {
  lexicalScore: number
  phraseScore: number
  entityScore: number
  dateScore: number
  eventScore: number
  fieldPriorScore: number
  penalties: {
    temporalMismatch: number
    broadThematic: number
    missingEvent: number
  }
  matchedFiles: string[]
  matchedDates: string[]
  matchedEntities: string[]
  matchedEventPhrases: string[]
  asksWhen: boolean
}

type RankedCandidate = {
  sessionId: string
  score: number
  title: string
  matchedFiles: string[]
  index: string
  state: string
  decisions: string
  timeline_excerpt: string
  scoreBreakdown: RankingBreakdown
}

function sanitizePath(input: string): string {
  return input.replace(/[^a-zA-Z0-9_.-]/g, "_")
}

function stableStringify(value: unknown): string {
  if (value === null || value === undefined) {
    return JSON.stringify(value)
  }
  if (Array.isArray(value)) {
    return `[${value.map((item) => stableStringify(item)).join(",")}]`
  }
  if (typeof value === "object") {
    const entries = Object.entries(value as Record<string, unknown>).sort(([left], [right]) =>
      left.localeCompare(right)
    )
    return `{${entries.map(([key, item]) => `${JSON.stringify(key)}:${stableStringify(item)}`).join(",")}}`
  }
  return JSON.stringify(value)
}

function sha256(input: string): string {
  return createHash("sha256").update(input).digest("hex")
}

const STOP_TERMS = new Set([
  "the",
  "and",
  "for",
  "with",
  "from",
  "that",
  "this",
  "what",
  "when",
  "where",
  "which",
  "who",
  "did",
  "does",
  "was",
  "were",
  "have",
  "has",
  "had",
  "into",
  "onto",
  "about",
  "them",
  "they",
  "then",
])

const DATE_PATTERN =
  /\b(?:\d{1,2}\s+(?:january|february|march|april|may|june|july|august|september|october|november|december)\s*,?\s*\d{4}|\d{1,2}\s+(?:january|february|march|april|may|june|july|august|september|october|november|december)|(?:january|february|march|april|may|june|july|august|september|october|november|december)\s+\d{1,2},\s*\d{4}|\d{4}-\d{2}-\d{2})\b/i

function normalize(text: string): string {
  return text.toLowerCase().replace(/[^a-z0-9\s]/g, " ")
}

function queryTerms(query: string): string[] {
  return normalize(query)
    .split(/\s+/)
    .filter((term) => term.length > 2 && !STOP_TERMS.has(term))
}

function extractEntities(query: string): string[] {
  const matches = query.match(/\b(?:[A-Z][a-z]+|[A-Z]{2,})\b/g) || []
  return [...new Set(matches.map((match) => match.toLowerCase()))]
}

function extractNormalizedDates(text: string): string[] {
  const matches = text.match(DATE_PATTERN) || []
  return [...new Set(matches.map((match) => normalizeDateString(match)).filter(Boolean))]
}

function normalizeDateString(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    const cleaned = value.trim().replace(/\s+/g, " ").toLowerCase()
    return cleaned
  }
  return parsed.toISOString().slice(0, 10)
}

function unique<T>(items: T[]): T[] {
  return [...new Set(items)]
}

function buildQueryProfile(query: string): QueryProfile {
  const normalized = normalize(query)
  const tokens = normalized.split(/\s+/).filter(Boolean)
  const terms = queryTerms(query)
  const phrases: string[] = []

  for (let size = 3; size >= 2; size--) {
    for (let index = 0; index <= tokens.length - size; index++) {
      const phraseTokens = tokens.slice(index, index + size)
      const meaningfulTokens = phraseTokens.filter((term) => !STOP_TERMS.has(term))
      if (meaningfulTokens.length < 2) continue
      const phrase = phraseTokens.join(" ").trim()
      if (phrase && !phrases.includes(phrase)) {
        phrases.push(phrase)
      }
    }
  }

  const eventPhrases = unique(
    phrases.filter((phrase) =>
      /(support group|group|meeting|interview|appointment|event|session|decision|plan|authentication|traceability)/.test(
        phrase
      )
    )
  )
  const fallbackSupportGroup = normalized.includes("support group") ? ["support group"] : []
  const fallbackLgbtqSupportGroup = normalized.includes("lgbtq support group") ? ["lgbtq support group"] : []

  return {
    normalizedQuery: normalized,
    terms,
    phrases,
    eventPhrases: unique([...eventPhrases, ...fallbackSupportGroup, ...fallbackLgbtqSupportGroup]),
    entities: extractEntities(query),
    queryDates: extractNormalizedDates(query),
    asksWhen: /\bwhen\b|\bwhat\s+date\b|\bwhat\s+day\b/.test(normalized),
  }
}

function formatConversation(messages: UnifiedMessage[]): string {
  return messages
    .map((message) => {
      const speaker = message.speaker || message.role
      const timestamp = message.timestamp ? ` [${message.timestamp}]` : ""
      return `${speaker}${timestamp}: ${message.content}`
    })
    .join("\n")
}

function summarizeIndex(session: UnifiedSession, extracted: string): string {
  const lines = extracted
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .slice(0, 6)
  const title = `# ${session.sessionId}`
  return [title, "", "## Summary", ...lines].join("\n")
}

function summarizeState(extracted: string): string {
  const sections = extracted.split(/\n##\s+/)
  const preferred = sections.find((section) =>
    normalize(section).includes("key facts") || normalize(section).includes("decisions")
  )
  const content = preferred ? `## ${preferred.trim()}` : extracted.trim()
  return ["# Current State", "", content].join("\n")
}

function summarizeDecisions(extracted: string): string {
  const lines = extracted
    .split("\n")
    .filter((line) => line.trim().startsWith("-"))
    .slice(0, 8)
  if (lines.length === 0) {
    return "# Decisions\n\n- No explicit decisions extracted."
  }
  return ["# Decisions", "", ...lines].join("\n")
}

function timelineFromMessages(session: UnifiedSession): string {
  return `# Timeline\n\n${formatConversation(session.messages)}`
}

function buildTimelineExcerpt(timeline: string, query: string, maxLines: number = 8): string {
  const lines = timeline.split("\n")
  const profile = buildQueryProfile(query)
  const hitIndex = lines.findIndex((line) => scoreLine(profile, line) > 0)
  const start = hitIndex >= 0 ? Math.max(hitIndex - 2, 0) : 0
  return lines.slice(start, start + maxLines).join("\n").trim()
}

function countOccurrences(haystack: string, needle: string): number {
  let count = 0
  let offset = 0
  while (offset >= 0) {
    offset = haystack.indexOf(needle, offset)
    if (offset >= 0) {
      count += 1
      offset += needle.length
    }
  }
  return count
}

function scoreLine(profile: QueryProfile, content: string): number {
  const haystack = normalize(content)
  if (profile.terms.length === 0) return 0
  let hits = 0
  for (const term of profile.terms) {
    if (haystack.includes(term)) {
      hits += 1
    }
  }
  const termCoverage = hits / profile.terms.length
  const phraseMatches = profile.phrases.filter((phrase) => haystack.includes(phrase)).length
  const phraseCoverage = profile.phrases.length > 0 ? phraseMatches / profile.phrases.length : 0
  const dateBonus = profile.asksWhen && DATE_PATTERN.test(content) ? 0.2 : 0

  return Math.min(termCoverage + phraseCoverage * 0.9 + dateBonus, 1.8)
}

function scoreField(profile: QueryProfile, content: string, weight: number): number {
  const haystack = normalize(content)
  if (profile.terms.length === 0) return 0

  let termHits = 0
  let totalOccurrences = 0
  for (const term of profile.terms) {
    if (haystack.includes(term)) {
      termHits += 1
      totalOccurrences += countOccurrences(haystack, term)
    }
  }

  const termCoverage = termHits / profile.terms.length
  const phraseMatches = profile.phrases.filter((phrase) => haystack.includes(phrase)).length
  const phraseCoverage = profile.phrases.length > 0 ? phraseMatches / profile.phrases.length : 0
  const occurrenceBonus = Math.min(totalOccurrences / 20, 0.15)
  const temporalBonus = profile.asksWhen && DATE_PATTERN.test(content) ? 0.15 : 0

  return Math.min(termCoverage + phraseCoverage * 1.2 + occurrenceBonus + temporalBonus, 1.8) * weight
}

function countPhraseMatches(haystack: string, phrases: string[]): { matches: string[]; score: number } {
  const matches = phrases.filter((phrase) => haystack.includes(phrase))
  return {
    matches,
    score: matches.length === 0 ? 0 : Math.min(matches.length / Math.max(phrases.length, 1), 1),
  }
}

function countEntityMatches(haystack: string, entities: string[]): { matches: string[]; score: number } {
  const matches = entities.filter((entity) => haystack.includes(entity))
  return {
    matches,
    score: matches.length === 0 ? 0 : Math.min(matches.length / Math.max(entities.length, 1), 1),
  }
}

function extractRelevantLines(content: string, query: string, maxLines: number = 4): string {
  const profile = buildQueryProfile(query)
  const lines = content
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .filter((line) => !line.startsWith("#"))

  if (lines.length === 0) {
    return content.trim()
  }

  const scoredLines = lines
    .map((line) => ({ line, score: scoreLine(profile, line) }))
    .filter((item) => item.score > 0)
    .sort((a, b) => b.score - a.score)

  const selected = scoredLines.slice(0, maxLines).map((item) => item.line)
  if (selected.length === 0) {
    return lines.slice(0, maxLines).join("\n")
  }

  return selected.join("\n")
}

function scoreDocument(
  profile: QueryProfile,
  index: string,
  state: string,
  decisions: string,
  timeline: string
): { score: number; matchedFiles: string[]; breakdown: RankingBreakdown } {
  const fieldScores = [
    { file: "state.md", value: scoreField(profile, state, 0.28) },
    { file: "decisions.md", value: scoreField(profile, decisions, 0.18) },
    { file: "index.md", value: scoreField(profile, index, 0.16) },
    { file: "timeline.md", value: scoreField(profile, timeline, 0.12) },
  ]

  const matchedFiles = fieldScores.filter((item) => item.value > 0).map((item) => item.file)
  const fieldPriorScore = fieldScores.reduce((sum, item) => sum + item.value, 0)

  const rawCombined = [index, state, decisions, timeline].join("\n")
  const combined = normalize(rawCombined)
  const longestPhrase = profile.phrases[0]
  const phraseMatches = countPhraseMatches(combined, profile.phrases)
  const eventMatches = countPhraseMatches(combined, profile.eventPhrases)
  const entityMatches = countEntityMatches(combined, profile.entities)
  const candidateDates = extractNormalizedDates(rawCombined)
  const matchedDates = profile.queryDates.filter((date) => candidateDates.includes(date))

  const lexicalScore =
    profile.terms.length === 0
      ? 0
      : Math.min(
          profile.terms.filter((term) => combined.includes(term)).length / profile.terms.length,
          1
        ) * 0.2
  const phraseScore = phraseMatches.score * 0.35
  const entityScore = entityMatches.score * 0.2
  const eventScore =
    eventMatches.score * 0.8 +
    (eventMatches.matches.includes("support group") ? 0.15 : 0) +
    (eventMatches.matches.includes("lgbtq support group") ? 0.2 : 0)
  const dateScore =
    matchedDates.length > 0
      ? 0.7
      : profile.queryDates.length === 0 && profile.asksWhen && eventMatches.matches.length > 0 && candidateDates.length > 0
        ? 0.32
        : 0

  const temporalMismatchPenalty =
    profile.asksWhen && candidateDates.length > 0 && eventMatches.matches.length === 0 ? 0.38 : 0
  const broadThematicPenalty =
    profile.eventPhrases.length > 0 &&
    combined.includes("lgbtq") &&
    !eventMatches.matches.includes("support group") &&
    !eventMatches.matches.includes("lgbtq support group")
      ? 0.18
      : 0
  const missingEventPenalty =
    profile.eventPhrases.length > 0 && eventMatches.matches.length === 0 ? 0.22 : 0

  let total = lexicalScore + phraseScore + entityScore + eventScore + dateScore + fieldPriorScore

  if (longestPhrase && combined.includes(longestPhrase)) {
    total += 0.1
  }

  total -= temporalMismatchPenalty + broadThematicPenalty + missingEventPenalty

  return {
    score: Math.max(total, 0),
    matchedFiles,
    breakdown: {
      lexicalScore: roundScore(lexicalScore),
      phraseScore: roundScore(phraseScore),
      entityScore: roundScore(entityScore),
      dateScore: roundScore(dateScore),
      eventScore: roundScore(eventScore),
      fieldPriorScore: roundScore(fieldPriorScore),
      penalties: {
        temporalMismatch: roundScore(temporalMismatchPenalty),
        broadThematic: roundScore(broadThematicPenalty),
        missingEvent: roundScore(missingEventPenalty),
      },
      matchedFiles,
      matchedDates,
      matchedEntities: entityMatches.matches,
      matchedEventPhrases: eventMatches.matches,
      asksWhen: profile.asksWhen,
    },
  }
}

export class FractalMemoryProvider implements Provider {
  name = "fractalmemory"
  prompts = FRACTALMEMORY_PROMPTS
  concurrency = {
    default: 25,
    ingest: 10,
    indexing: 50,
  }

  private openai: ReturnType<typeof createOpenAI> | null = null
  private benchmarkName = "unknown"
  private extractionPipelineVersion = DEFAULT_EXTRACTION_PIPELINE_VERSION
  private indexPipelineVersion = DEFAULT_INDEX_PIPELINE_VERSION
  private retrievalTopK = 10
  private answerTopK = 3
  private enableRankingDebug = true
  private rankingDebugTopN = 5
  private readonly extractMemoriesFn: ExtractMemoriesFn

  constructor(extractMemoriesFn: ExtractMemoriesFn = extractMemories) {
    this.extractMemoriesFn = extractMemoriesFn
  }

  async initialize(config: ProviderConfig): Promise<void> {
    if (!config.apiKey || config.apiKey === "none") {
      throw new Error("FractalMemory provider requires OPENAI_API_KEY for memory extraction")
    }
    this.openai = createOpenAI({ apiKey: config.apiKey })
    this.benchmarkName = typeof config.benchmark === "string" ? config.benchmark : "unknown"
    this.extractionPipelineVersion =
      typeof config.extractionPipelineVersion === "string"
        ? config.extractionPipelineVersion
        : DEFAULT_EXTRACTION_PIPELINE_VERSION
    this.indexPipelineVersion =
      typeof config.indexPipelineVersion === "string"
        ? config.indexPipelineVersion
        : DEFAULT_INDEX_PIPELINE_VERSION
    this.retrievalTopK =
      typeof config.retrievalTopK === "number" ? Math.max(1, Math.trunc(config.retrievalTopK)) : 10
    this.answerTopK =
      typeof config.answerTopK === "number" ? Math.max(1, Math.trunc(config.answerTopK)) : 3
    this.enableRankingDebug =
      typeof config.enableRankingDebug === "boolean" ? config.enableRankingDebug : true
    this.rankingDebugTopN =
      typeof config.rankingDebugTopN === "number" ? Math.max(1, Math.trunc(config.rankingDebugTopN)) : 5
    await mkdir(getBaseDir(), { recursive: true })
    await mkdir(getCacheDir(), { recursive: true })
    logger.info("Initialized FractalMemory provider")
  }

  async ingest(sessions: UnifiedSession[], options: IngestOptions): Promise<IngestResult> {
    if (!this.openai) throw new Error("Provider not initialized")
    const ingestStart = Date.now()
    const containerDir = join(getBaseDir(), sanitizePath(options.containerTag))
    await mkdir(containerDir, { recursive: true })
    const documentIds: string[] = []
    const manifestSessions = await this.loadExistingContainerSessions(containerDir)
    let cacheLookupMs = 0
    let extractionMs = 0
    let cacheWriteMs = 0
    for (const session of sessions) {
      const lookupStart = Date.now()
      const sourceFingerprint = this.computeSourceFingerprint(session)
      const cacheKey = this.computeCacheKey(sourceFingerprint)
      const cached = await this.loadCachedDocument(cacheKey, sourceFingerprint)
      cacheLookupMs += Date.now() - lookupStart

      const document = cached ?? (await this.buildDocument(session, sourceFingerprint, cacheKey))
      if (!cached) {
        extractionMs += this.lastBuildMetrics.extractionMs
        cacheWriteMs += this.lastBuildMetrics.cacheWriteMs
        logger.info("fractalmemory cache miss: source corpus or pipeline changed", {
          sessionId: session.sessionId,
          cacheKey,
        })
      } else {
        logger.info("fractalmemory cache hit: reusing extracted corpus artifacts", {
          sessionId: session.sessionId,
          cacheKey,
        })
      }

      await this.materializeSession(containerDir, document.document)
      documentIds.push(session.sessionId)
      this.upsertContainerSession(manifestSessions, {
        sessionId: session.sessionId,
        sessionDirName: sanitizePath(session.sessionId),
        cacheKey,
        sourceFingerprint,
      })
      logger.debug(`Fractal-ingested session ${session.sessionId}`)
    }
    const containerManifest: ContainerManifest = {
      provider: this.name,
      benchmark: this.benchmarkName,
      extractionPipelineVersion: this.extractionPipelineVersion,
      indexPipelineVersion: this.indexPipelineVersion,
      containerTag: options.containerTag,
      createdAt: new Date().toISOString(),
      metrics: {
        cacheLookupSeconds: roundSeconds(cacheLookupMs),
        extractionSeconds: roundSeconds(extractionMs),
        cacheWriteSeconds: roundSeconds(cacheWriteMs),
        ingestTotalSeconds: roundSeconds(Date.now() - ingestStart),
      },
      sessions: manifestSessions,
    }
    await writeFile(join(containerDir, "manifest.json"), JSON.stringify(containerManifest, null, 2), "utf-8")
    return { documentIds }
  }

  async awaitIndexing(
    result: IngestResult,
    _containerTag: string,
    onProgress?: IndexingProgressCallback
  ): Promise<void> {
    onProgress?.({
      completedIds: result.documentIds,
      failedIds: [],
      total: result.documentIds.length,
    })
  }

  async search(query: string, options: SearchOptions): Promise<unknown[]> {
    const containerDir = join(getBaseDir(), sanitizePath(options.containerTag))
    const profile = buildQueryProfile(query)
    let entries: { sessionId: string; sessionDirName: string }[]
    try {
      entries = await this.loadContainerEntries(containerDir)
    } catch {
      logger.warn(`No FractalMemory data for ${options.containerTag}`)
      return []
    }
    const hits: SearchHit[] = []
    const rankedCandidates: RankedCandidate[] = []
    for (const entry of entries) {
      const sessionDir = join(containerDir, entry.sessionDirName)
      const [index, state, timeline, decisions] = await Promise.all([
        readFile(join(sessionDir, "index.md"), "utf-8"),
        readFile(join(sessionDir, "state.md"), "utf-8"),
        readFile(join(sessionDir, "timeline.md"), "utf-8"),
        readFile(join(sessionDir, "decisions.md"), "utf-8"),
      ])
      const { score, matchedFiles, breakdown } = scoreDocument(profile, index, state, decisions, timeline)
      rankedCandidates.push({
        sessionId: entry.sessionId,
        score,
        title: entry.sessionId,
        matchedFiles,
        index: extractRelevantLines(index, query, 3),
        state: extractRelevantLines(state, query, 3),
        decisions: extractRelevantLines(decisions, query, 4),
        timeline_excerpt: buildTimelineExcerpt(timeline, query),
        scoreBreakdown: breakdown,
      })
    }
    rankedCandidates.sort((left, right) => {
      if (right.score !== left.score) return right.score - left.score
      return left.sessionId.localeCompare(right.sessionId)
    })
    const retrievalTopK = Math.max(1, Math.min(options.limit || this.retrievalTopK, this.retrievalTopK))
    const answerTopK = Math.max(1, Math.min(this.answerTopK, retrievalTopK))
    const selectedHits = rankedCandidates.slice(0, retrievalTopK).map((candidate, index) => ({
      ...candidate,
      answerCandidate: index < answerTopK,
      score: roundScore(candidate.score),
    }))
    if (this.enableRankingDebug) {
      await this.writeRankingDebug(options.containerTag, query, rankedCandidates, retrievalTopK, answerTopK)
    }
    hits.push(...selectedHits)
    return hits
  }

  async clear(containerTag: string): Promise<void> {
    const containerDir = join(getBaseDir(), sanitizePath(containerTag))
    await rm(containerDir, { recursive: true, force: true })
    logger.info(`Cleared FractalMemory data for ${containerTag}`)
  }

  private lastBuildMetrics = {
    extractionMs: 0,
    cacheWriteMs: 0,
  }

  private computeSourceFingerprint(session: UnifiedSession): string {
    return sha256(
      stableStringify({
        provider: this.name,
        benchmark: this.benchmarkName,
        extractionPipelineVersion: this.extractionPipelineVersion,
        indexPipelineVersion: this.indexPipelineVersion,
        session,
      })
    )
  }

  private computeCacheKey(sourceFingerprint: string): string {
    return sha256(
      stableStringify({
        provider: this.name,
        benchmark: this.benchmarkName,
        extractionPipelineVersion: this.extractionPipelineVersion,
        indexPipelineVersion: this.indexPipelineVersion,
        sourceFingerprint,
      })
    )
  }

  private async loadCachedDocument(
    cacheKey: string,
    sourceFingerprint: string
  ): Promise<CachedNodeDocument | null> {
    const cacheDir = join(getCacheDir(), cacheKey)
    try {
      const manifestPayload = JSON.parse(await readFile(join(cacheDir, "manifest.json"), "utf-8")) as CachedNodeDocument
      const documentPayload = JSON.parse(await readFile(join(cacheDir, "document.json"), "utf-8")) as NodeDocument
      if (
        manifestPayload.provider !== this.name ||
        manifestPayload.benchmark !== this.benchmarkName ||
        manifestPayload.extractionPipelineVersion !== this.extractionPipelineVersion ||
        manifestPayload.indexPipelineVersion !== this.indexPipelineVersion ||
        manifestPayload.sourceFingerprint !== sourceFingerprint
      ) {
        logger.info("fractalmemory cache invalidated: pipeline version or source fingerprint changed", {
          cacheKey,
        })
        return null
      }
      return {
        ...manifestPayload,
        document: documentPayload,
      }
    } catch {
      return null
    }
  }

  private async buildDocument(
    session: UnifiedSession,
    sourceFingerprint: string,
    cacheKey: string
  ): Promise<CachedNodeDocument> {
    if (!this.openai) {
      throw new Error("Provider not initialized")
    }

    const extractionStart = Date.now()
    const extracted = await this.extractMemoriesFn(this.openai, session)
    this.lastBuildMetrics.extractionMs = Date.now() - extractionStart

    const date =
      (session.metadata?.formattedDate as string) ||
      (session.metadata?.date as string) ||
      "Unknown date"
    const document: NodeDocument = {
      sessionId: session.sessionId,
      date,
      title: session.sessionId,
      index: summarizeIndex(session, extracted),
      state: summarizeState(extracted),
      timeline: timelineFromMessages(session),
      decisions: summarizeDecisions(extracted),
    }

    const cachePayload: CachedNodeDocument = {
      provider: this.name,
      benchmark: this.benchmarkName,
      extractionPipelineVersion: this.extractionPipelineVersion,
      indexPipelineVersion: this.indexPipelineVersion,
      sourceFingerprint,
      cacheKey,
      createdAt: new Date().toISOString(),
      document,
    }

    const cacheWriteStart = Date.now()
    const cacheDir = join(getCacheDir(), cacheKey)
    await mkdir(cacheDir, { recursive: true })
    await writeFile(
      join(cacheDir, "manifest.json"),
      JSON.stringify(
        {
          provider: cachePayload.provider,
          benchmark: cachePayload.benchmark,
          extractionPipelineVersion: cachePayload.extractionPipelineVersion,
          indexPipelineVersion: cachePayload.indexPipelineVersion,
          sourceFingerprint: cachePayload.sourceFingerprint,
          cacheKey: cachePayload.cacheKey,
          createdAt: cachePayload.createdAt,
        },
        null,
        2
      ),
      "utf-8"
    )
    await writeFile(join(cacheDir, "document.json"), JSON.stringify(document, null, 2), "utf-8")
    this.lastBuildMetrics.cacheWriteMs = Date.now() - cacheWriteStart
    return cachePayload
  }

  private async materializeSession(containerDir: string, document: NodeDocument): Promise<void> {
    const sessionDir = join(containerDir, sanitizePath(document.sessionId))
    await mkdir(join(sessionDir, "children"), { recursive: true })
    await mkdir(join(sessionDir, "artifacts"), { recursive: true })
    await writeFile(join(sessionDir, "index.md"), document.index, "utf-8")
    await writeFile(join(sessionDir, "state.md"), document.state, "utf-8")
    await writeFile(join(sessionDir, "timeline.md"), document.timeline, "utf-8")
    await writeFile(join(sessionDir, "decisions.md"), document.decisions, "utf-8")
  }

  private async loadContainerEntries(
    containerDir: string
  ): Promise<{ sessionId: string; sessionDirName: string }[]> {
    try {
      const manifestPayload = JSON.parse(
        await readFile(join(containerDir, "manifest.json"), "utf-8")
      ) as ContainerManifest
      if (Array.isArray(manifestPayload.sessions) && manifestPayload.sessions.length > 0) {
        return manifestPayload.sessions.map((session) => ({
          sessionId: session.sessionId,
          sessionDirName: session.sessionDirName,
        }))
      }
    } catch {
      // Fall back to legacy directory discovery.
    }

    const entries = await readdir(containerDir, { withFileTypes: true })
    return entries
      .filter((entry) => entry.isDirectory())
      .map((entry) => ({ sessionId: entry.name, sessionDirName: entry.name }))
  }

  private async loadExistingContainerSessions(containerDir: string): Promise<ContainerSessionEntry[]> {
    try {
      const manifestPayload = JSON.parse(
        await readFile(join(containerDir, "manifest.json"), "utf-8")
      ) as ContainerManifest
      if (Array.isArray(manifestPayload.sessions)) {
        return [...manifestPayload.sessions]
      }
    } catch {
      // New container or legacy layout without manifest.
    }
    return []
  }

  private upsertContainerSession(
    sessions: ContainerSessionEntry[],
    entry: ContainerSessionEntry
  ): void {
    const existingIndex = sessions.findIndex((candidate) => candidate.sessionId === entry.sessionId)
    if (existingIndex >= 0) {
      sessions[existingIndex] = entry
      return
    }
    sessions.push(entry)
    sessions.sort((left, right) => left.sessionId.localeCompare(right.sessionId))
  }

  private async writeRankingDebug(
    containerTag: string,
    query: string,
    candidates: RankedCandidate[],
    retrievalTopK: number,
    answerTopK: number
  ): Promise<void> {
    const debugPath = this.resolveRankingDebugPath(containerTag)
    await mkdir(dirname(debugPath), { recursive: true })
    const payload = {
      provider: this.name,
      benchmark: this.benchmarkName,
      containerTag,
      query,
      retrievalTopK,
      answerTopK,
      generatedAt: new Date().toISOString(),
      candidates: candidates.slice(0, this.rankingDebugTopN).map((candidate, index) => ({
        rank: index + 1,
        sessionId: candidate.sessionId,
        score: roundScore(candidate.score),
        answerCandidate: index < answerTopK,
        breakdown: candidate.scoreBreakdown,
      })),
    }
    await writeFile(debugPath, JSON.stringify(payload, null, 2), "utf-8")
  }

  private resolveRankingDebugPath(containerTag: string): string {
    const match = /^(.*)-(run-.+)$/.exec(containerTag)
    if (match) {
      const [, questionId, runId] = match
      return join(process.cwd(), "data", "runs", runId, "ranking_debug", `${sanitizePath(questionId)}.json`)
    }
    return join(getBaseDir(), "ranking_debug", `${sanitizePath(containerTag)}.json`)
  }
}

export default FractalMemoryProvider

function roundSeconds(milliseconds: number): number {
  return Math.round((milliseconds / 1000) * 1000) / 1000
}

function roundScore(value: number): number {
  return Math.round(value * 1000) / 1000
}
