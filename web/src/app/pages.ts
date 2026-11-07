// New names everywhere; the old name stays in the markup as data-was so testers can map screens back to today's app.
// The sidebar shows them in three sections: the project, its crew & costs, then pricing.
export const PAGE_GROUPS = [
  { key: 'project', label: 'Project' },
  { key: 'costs', label: 'Crew & costs' },
  { key: 'pricing', label: 'Pricing' },
] as const
export const PROJECT_PAGES = [
  { key: 'profile', label: 'Project profile', icon: 'sliders', was: 'Project setup / Project information', group: 'project' },
  { key: 'crew-plan', label: 'Crew plan', icon: 'team', was: 'Project team', group: 'costs' },
  { key: 'precon-services', label: 'Precon services', icon: 'layers', was: 'Preconstruction', group: 'costs' },
  { key: 'overheads', label: 'Project overheads', icon: 'cone', was: 'General requirements', group: 'costs' },
  { key: 'bid-board', label: 'Bid board', icon: 'scale', was: 'Bid leveling / Trade summary', group: 'pricing' },
  { key: 'scope-match', label: 'Scope match', icon: 'list', was: 'Scope leveling', group: 'pricing' },
  { key: 'base-requirements', label: 'Base requirements', icon: 'check', was: 'General scope', group: 'pricing' },
  { key: 'price-roll-up', label: 'Price roll-up', icon: 'chart', was: 'Executive summary', group: 'pricing' },
  { key: 'alternates', label: 'Alternates & VE', icon: 'fork', was: 'Alternates / Value engineering', group: 'pricing', feature: 'options' },
  { key: 'allowances', label: 'Allowances', icon: 'wallet', was: 'Allowances', group: 'pricing', feature: 'provisionalsums' },
  { key: 'unit-prices', label: 'Unit prices', icon: 'tag', was: 'Unit prices', group: 'pricing', feature: 'unitrates' },
] as const
/** Pages that only show when their feature is switched on (Setting feature.*). */
export const pageOn = (p: object, f?: Record<string, boolean> | null) => { const k = (p as { feature?: string }).feature; return !k || !!f?.[k] }
export type PageKey = typeof PROJECT_PAGES[number]['key']
