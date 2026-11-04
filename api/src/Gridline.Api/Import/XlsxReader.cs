using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Gridline.Import;

/// <summary>
/// Streams rows out of an .xlsx without loading the sheet into memory (SAX reader), so the
/// import handles workbooks with hundreds of thousands of rows.
/// </summary>
public sealed class XlsxReader : IDisposable
{
    readonly SpreadsheetDocument _doc;
    readonly string[] _shared;
    readonly HashSet<uint> _dateStyles = [];

    public XlsxReader(Stream stream)
    {
        _doc = SpreadsheetDocument.Open(stream, false);
        var sst = _doc.WorkbookPart?.SharedStringTablePart?.SharedStringTable;
        _shared = sst is null ? [] : sst.Elements<SharedStringItem>().Select(i => i.InnerText).ToArray();
        var styles = _doc.WorkbookPart?.WorkbookStylesPart?.Stylesheet;
        if (styles?.CellFormats is { } cf)
        {
            var custom = styles.NumberingFormats?.Elements<NumberingFormat>()
                .Where(n => n.FormatCode?.Value is string f && LooksLikeDate(f)).Select(n => n.NumberFormatId!.Value).ToHashSet() ?? [];
            uint i = 0;
            foreach (var f in cf.Elements<CellFormat>())
            {
                var id = f.NumberFormatId?.Value ?? 0;
                if (id is >= 14 and <= 22 or >= 45 and <= 47 || custom.Contains(id)) _dateStyles.Add(i);
                i++;
            }
        }
    }

    static bool LooksLikeDate(string f)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(f, "\"[^\"]*\"|\\[[^\\]]*\\]", "");
        return s.IndexOfAny(['d', 'y', 'm']) >= 0 && !s.Contains('0') && !s.Contains('#');
    }

    public IReadOnlyList<string> SheetNames =>
        _doc.WorkbookPart?.Workbook.Sheets?.Elements<Sheet>().Select(s => s.Name?.Value ?? "").ToList() ?? [];

    /// <summary>Rows of a sheet as (rowNo, cells by column index). Empty rows are skipped.</summary>
    public IEnumerable<(int rowNo, Dictionary<int, object?> cells)> Rows(string sheetName)
    {
        var sheet = _doc.WorkbookPart?.Workbook.Sheets?.Elements<Sheet>()
            .FirstOrDefault(s => string.Equals(s.Name?.Value?.Trim(), sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet?.Id?.Value is not string rid) yield break;
        var part = (WorksheetPart)_doc.WorkbookPart!.GetPartById(rid);
        using var reader = OpenXmlReader.Create(part);
        var autoRow = 0;
        while (reader.Read())
        {
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement) continue;
            var row = (Row)reader.LoadCurrentElement()!;
            var rowNo = (int)(row.RowIndex?.Value ?? (uint)(autoRow + 1)); autoRow = rowNo;
            var cells = new Dictionary<int, object?>();
            var autoCol = 0;
            foreach (var c in row.Elements<Cell>())
            {
                var col = c.CellReference?.Value is string r ? ColumnIndex(r) : autoCol + 1; autoCol = col;
                var v = Value(c);
                if (v is string s && s.Length == 0) v = null;
                if (v is not null) cells[col] = v;
            }
            if (cells.Count > 0) yield return (rowNo, cells);
        }
    }

    object? Value(Cell c)
    {
        var raw = c.CellValue?.Text;
        var t = c.DataType?.Value;
        if (t == CellValues.InlineString) return c.InlineString?.InnerText?.Trim();
        if (raw is null) return null;
        if (t == CellValues.SharedString) return int.TryParse(raw, out var i) && i < _shared.Length ? _shared[i].Trim() : null;
        if (t == CellValues.String) return raw.Trim();
        if (t == CellValues.Boolean) return raw == "1";
        if (t == CellValues.Error) return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            if (c.StyleIndex?.Value is uint st && _dateStyles.Contains(st) && d is > 0 and < 2958466) return DateTime.FromOADate(d);
            return (decimal)d;
        }
        return raw.Trim();
    }

    public static int ColumnIndex(string cellRef)
    {
        var n = 0;
        foreach (var ch in cellRef) { if (!char.IsLetter(ch)) break; n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1); }
        return n;
    }

    public void Dispose() => _doc.Dispose();
}
