// Shapes returned by the API (camelCase JSON).
export type Stage = 'lead' | 'pursuit' | 'bidding' | 'submitted' | 'awarded' | 'lost' | 'archived'
export type BidState = 'invited' | 'bidding' | 'posted' | 'late' | 'incomplete' | 'notplansspecs' | 'undecided' | 'disqualified' | 'declined'
export type AnswerKind = 'included' | 'excluded' | 'amount' | 'price' | 'text' | 'blank'
export type Desired = 'included' | 'excluded' | 'price' | 'text'
export interface Me { userId: number; name: string; initials: string; colour: string; email: string; roles: string[]; capabilities: string[]; company: { id: number; name: string; slug: string; plan: string; trialEndsOn: string | null } }
export interface ProjectRow { projectId: number; projectNo: string; projectName: string; stage: Stage; clientName: string | null; projectType: string | null; city: string | null; stateCode: string | null; bidDueOn: string | null; primaryVersionNo: number | null; grossArea: number; trades: number; tradesCarried: number; tradesTotal: number | null; contractTotal: number | null; estimator: string | null; estimatorId: number | null; isMember: boolean; isFavourite: boolean; peopleHere: number }
export interface Version { versionId: number; versionNo: number; versionLabel: string | null; isPrimary: boolean; status: string; grossArea: number; buildMonths: number | null; feePct: number; baselineName: string | null; copiedFromVersionId: number | null; createdAt: string; rowRev: number
  /** zones equal total project area (gross area = Σ zone SF not excluded) */ zonesSetArea: boolean; primaryAreaUnit: AreaUnit; primaryUnitLabel: UnitLabel; totalUnits: number }
export type AreaUnit = 'SF' | 'RSF' | 'GSF' | 'USF'
export type UnitLabel = 'N/A' | 'Unit' | 'MW' | 'Key' | 'Space'
/** Was: breakout area. isRemainder = "Remainder of base bid" (its amount is the bid total minus the other zones). */
export interface Zone { zoneId: number; zoneCode: string; zoneName: string; sortOrder: number; floorArea: number; areaUnit: AreaUnit; unitCount: number | null; unitLabel: UnitLabel; defaultShare: number | null; colour: string | null
  isRemainder: boolean; excludeFromTotal: boolean; projectReservePct: number; designReservePct: number; escalationReservePct: number }
export interface ScopeZone { zoneId: number; zoneCode: string; zoneName: string; floorArea: number; areaUnit: AreaUnit; unitCount: number | null; unitLabel: UnitLabel; isRemainder: boolean; colour: string | null }
export interface ProjectDetail { projectId: number; projectNo: string; projectName: string; stage: Stage; clientName: string | null; projectType: string | null; contractType: string | null; divisionId: number | null; branchId: number | null; street: string | null; city: string | null; stateCode: string | null; countyName: string | null; postalCode: string | null; bidDueOn: string | null; crmRef: string | null; estimatorId: number | null; managerId: number | null; rowRev: number; versions: Version[]
  /** schedule dates (was: Project setup dates) — the crew plan's weeks follow them */ preconStartOn: string | null; preconEndOn: string | null; buildStartOn: string | null; buildEndOn: string | null; closeoutOn: string | null; salaryEscalationPct: number; access: 'viewer' | 'editor' | 'owner' }
/** A trade partner's bid on a trade (a lane: A, B, C …). isCarried = picked. */
export interface Bid { bidId: number; tradeId: number; laneNo: number; bidderName: string; partnerId: number | null; state: BidState; isPosted: boolean; isCarried: boolean; baseAmount: number | null; scopeAdjustments: number; totalAmount: number | null; addedByUser: boolean; baseSource: string | null; email: string | null; contact: string | null; rank: number | null; rowRev: number; hiddenInScope: boolean }
export interface Benchmark { csiCode: string; lowPerSf: number; medianPerSf: number; highPerSf: number; projects: number }
/** carriedValue = picked value (picked bid total + adjustment, or the plug). */
export interface Trade { tradeId: number; versionId: number; csiCode: string; codeId: number | null; tradeName: string; scopeNo: number; kitId: number | null; estimatorId: number | null; bidDueOn: string | null; lowestValue: number | null; carriedValue: number | null; adjustment: number; budgetValue: number | null; budgetDelta: number | null; buyoutPct: number | null; plugAmount: number | null; clientNote: string | null; teamNote: string | null; sortOrder: number | null; rowRev: number; changedAt: string; changedByName: string | null; zonesFromScope: boolean; bids: Bid[]; zoneShares: Record<string, number>; benchmark: Benchmark | null }
export interface BidBoardData { version: Version; zones: Zone[]; trades: Trade[]; budgets: { budgetColumnId: number; budgetName: string; values: number }[]; baselineName: string | null; totalsHash: string | null }
export interface TradeReply { trade: Trade; totalsHash: string; matched: boolean | null }
export interface ScopeLine { lineId: number; section: 'baserequirement' | 'tradeitem' | 'zone'; sortKey: number; isHeading: boolean; lineText: string; desired: Desired | null; skipInAdjustments: boolean; indent: number; lineNote: string | null; zoneId: number | null; requirementLineId: number | null; rowRev: number }
export interface ScopeAnswer { lineId: number; bidId: number; kind: AnswerKind; amount: number | null; answerText: string | null; formula: string | null; cellColour: 'red' | 'yellow' | 'green' | null; note: string | null; source: string; rowRev: number; changedAt: string; changedByName: string | null }
export interface Invite { inviteId: number; bidId: number | null; partnerId: number; companyName: string; email: string; status: string; message?: string | null; sentAt: string | null; openedAt: string | null; submittedAt: string | null; expiresAt: string; pendingSubmissionId?: number | null; submissionId?: number | null; decision?: string | null }
export interface ReadingRow { readingId: number; bidId: number; status: string; finishedAt: string | null; pending: number }
export interface ScopeData { tradeId: number; trade: Trade; lines: ScopeLine[]; answers: ScopeAnswer[]; zones: ScopeZone[]; invites: Invite[]; readings: ReadingRow[] }
export interface SummaryLineDto { name: string; kind: string; code?: string | null; ratePct?: number | null; amount: number; perArea: number | null; pctOfContract: number | null; zones: Record<string, number>; budgetAmount?: number | null; editable?: boolean; isOff?: boolean }
export interface MarkupRow { code: string; name: string; appliesTo: string; sortOrder: number; ratePct: number; fixedAmount: number | null; budgetAmount: number | null; defaultRatePct: number | null; isOff: boolean }
export interface RatesData { markups: MarkupRow[]; costs: { code: 'genreq' | 'precon' | 'genconds'; name: string; isOff: boolean }[] }
/** Fee carve-outs (was: fee exclusions). feeExcludes: genreq, precon, reserves, bond, cover. */
export interface CarveOuts { applyFeeExcludes: boolean; feeExcludes: string[]; excludeGenreqFromDirect: boolean; reservePrecon: string[]; reserveGenconds: string[]; feePct: number; feeOff: boolean }
export interface IndirectRow { indirectId: number; itemName: string; amount: number; outsideFee: boolean; sortOrder: number; description: string | null; isOff: boolean }
export interface RollUpData { versionId: number; totalsHash: string; grossArea: number; lines: SummaryLineDto[]; zones: Zone[]; indirect: IndirectRow[]; markups: MarkupRow[]; zoneReserves: string[]; carveOuts: CarveOuts
  input: { tradesTotal: number; genreqTotal: number; preconTotal: number; grossArea: number; markups: { code: string; name: string; appliesTo: 'direct' | 'subtotal' | 'contract' | 'tax'; ratePct: number; fixedAmount?: number | null; sortOrder: number }[]; indirect: { name: string; amount: number; outsideFee?: boolean }[]; zoneShares?: Record<string, number> | null
    options?: { feeExcludes?: string[]; excludeGenreqFromDirect?: boolean; reservePrecon?: string[]; reserveGenconds?: string[] }; gencondsTotal?: number } }
export type TeamGroup = 'operations' | 'siteops' | 'corporate'
export const TEAM_GROUPS: [TeamGroup, string][] = [['operations', 'Operations'], ['siteops', 'Site operations'], ['corporate', 'Corporate resources']]
export interface CrewWeek { weekId: number; weekNo: number; startsOn: string; phase: 'build' | 'closeout'; monthNo: number; year: number; multiplier: number }
/** A crew plan member (was: project team member). Costs escalate by year; quoted = actual × % quoted. */
export interface CrewMember { teamMemberId: number; staffId: number | null; staffName: string | null; staffRoleId: number | null; roleName: string | null; roleLabel: string; teamGroup: TeamGroup; sortKey: number
  hourlyCost: number | null; staffRate: number | null; roleRate: number | null; quotedPct: number
  perDiemPlaceId: number | null; perDiemPlace: string | null; perDiemStart: string | null; perDiemEnd: string | null; perDiemDays: 'weekdays' | 'alldays'; perDiemKind: 'hotel' | 'meals' | 'both'; perDiemDaysCount: number; perDiemCost: number
  relocationCost: number; note: string | null; rowRev: number; hours: Record<string, number>
  constructionWeeks: number; closeoutWeeks: number; constructionHours: number; closeoutHours: number; totalHours: number; constructionTimePct: number
  constructionCost: number; closeoutCost: number; totalCost: number; constructionQuoted: number; closeoutQuoted: number; totalQuoted: number
  finalRate: number | null; weeklyRate: number | null; quotedRate: number | null; quotedWeeklyRate: number | null; quotedOfCostPct: number | null; isTba: boolean }
export interface CrewTotals { hours: number; cost: number; quoted: number }
export interface ScheduleDates { preconStartOn: string | null; preconEndOn: string | null; buildStartOn: string | null; buildEndOn: string | null; closeoutOn: string | null }
export interface CrewData { projectId: number; salaryEscalationPct: number; schedule: ScheduleDates & { preconWeeks: number; buildWeeks: number; closeoutWeeks: number }
  weeks: CrewWeek[]; members: CrewMember[]; totals: { construction: CrewTotals; closeout: CrewTotals; total: CrewTotals; perDiem: number; relocation: number }; columns: string[] | null }
export interface PerDiemPlace { placeId: number; city: string; county: string | null; stateCode: string | null; locationDefined: string | null; lodging: number[]; meals: number; fiscalYear: number }
export interface PreconTask { activityId: number; taskName: string; isAdhoc: boolean }
export interface PreconMemberRow { preconMemberId: number; staffId: number | null; staffName: string | null; staffRoleId: number | null; roleName: string | null; roleLabel: string; teamGroup: TeamGroup
  sortKey: number; postingRate: number; quotedRate: number | null; rowRev: number; hours: Record<string, number>; totalHours: number; labour: number; isTba: boolean }
export interface PreconTeam { versionId: number; tasks: PreconTask[]; members: PreconMemberRow[]; totals: { hours: number; labour: number } }
export interface CostLineRow { costLineId: number; categoryName: string; itemName: string; sortKey: number; qty: number; uomCode: string | null; rate: number; quotedRate: number | null; isBillable: boolean
  actualTotal: number; quotedTotal: number; note: string | null; csiCode: string | null; csiTitle: string | null; rowRev: number; changedAt: string; changedByName: string | null }
/** Precon services expenses / Project overheads. quoted = (labour + billable) × Quoted % (precon) or Σ billable × % (overheads). */
export interface CostsData { versionId: number; area: 'precon' | 'genreq'; lines: CostLineRow[]; quotedPct: number
  totals: { labourActual: number; expensesActual: number; billableActual: number; actual: number; quoted: number; difference: number } }
export interface Lookups {
  states: { stateCode: string; stateName: string; salesTaxRate: number }[]; divisions: { divisionId: number; shortCode: string; divisionName: string }[]; branches: { branchId: number; branchName: string }[]
  staffRoles: { staffRoleId: number; roleName: string; defaultRate: number | null }[]; staff: { staffId: number; fullName: string; staffRoleId: number | null; hourlyCost: number | null; billRate: number | null; teamGroup: TeamGroup }[]
  users: { userId: number; name: string; initials: string; colour: string; email: string }[]; units: { uomCode: string; uomName: string }[]; csiCodes: { codeId: number; csiCode: string; codeTitle: string }[]
  markupTypes: { code: string; name: string; appliesTo: string; sortOrder: number; defaultRatePct: number }[]; scopeKits: { kitId: number; kitName: string; csiCode: string | null; lines: number }[]
  costKits: { costKitId: number; kitName: string; area: string; isDefault: boolean; items: number }[]; travelRates: { travelRateId: number; city: string; stateCode: string; daily: number }[]
  stages: string[]; bidStates: string[]; projectTypes: { projectType: string }[]
}
export interface PresenceRow { connectionId: string; userId: number; personName: string; initials: string; colour: string; projectId: number | null; versionId: number | null; pageKey: string | null; focusKind: string | null; focusKey: string | null; activity: string }
