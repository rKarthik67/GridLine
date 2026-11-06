// CSI MasterFormat division titles (2018), for grouping trades on the price roll-up and exports.
export const CSI_DIVISIONS: Record<string, string> = {
  '00': 'Procurement and contracting', '01': 'General requirements', '02': 'Existing conditions', '03': 'Concrete', '04': 'Masonry', '05': 'Metals',
  '06': 'Wood, plastics and composites', '07': 'Thermal and moisture protection', '08': 'Openings', '09': 'Finishes', '10': 'Specialties', '11': 'Equipment',
  '12': 'Furnishings', '13': 'Special construction', '14': 'Conveying equipment', '21': 'Fire suppression', '22': 'Plumbing', '23': 'HVAC',
  '25': 'Integrated automation', '26': 'Electrical', '27': 'Communications', '28': 'Electronic safety and security', '31': 'Earthwork', '32': 'Exterior improvements',
  '33': 'Utilities', '34': 'Transportation', '35': 'Waterway and marine', '40': 'Process interconnections', '41': 'Material processing and handling', '42': 'Process heating, cooling and drying',
  '43': 'Process gas and liquid handling', '44': 'Pollution and waste control', '45': 'Industry-specific manufacturing', '46': 'Water and wastewater', '48': 'Electrical power generation',
}
/** '03 30 00' → '03'; anything odd → '??' */
export const divisionOf = (csi: string) => { const m = /^\s*(\d{2})/.exec(csi ?? ''); return m ? m[1] : '??' }
export const divisionName = (d: string) => CSI_DIVISIONS[d] ?? (d === '??' ? 'Other' : `Division ${d}`)
