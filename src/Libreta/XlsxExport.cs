using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Libreta;

/// <summary>The whole book as one workbook: a sheet per view in navigation order, each cell holding the value the page
/// shows and formatted by its units.</summary>
public static class XlsxExport
{
  private const int SheetNameLimit = 31;

  public static void Write(string root, Stream output)
  {
    Book book = Book.Load(root);
    ViewCatalog catalog = CatalogLoader.Load(book, 1);
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var styles = new StyleTable();
    List<ExportSheet> sheets = catalog.Views
      .Select(entry => Layout(book.Views[entry.Id], SheetName(entry.Title, used), styles))
      .ToList();

    using SpreadsheetDocument document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
    WorkbookPart workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();
    workbookPart.AddNewPart<WorkbookStylesPart>().Stylesheet = styles.Stylesheet();
    Sheets sheetList = workbookPart.Workbook.AppendChild(new Sheets());
    uint sheetId = 1;
    foreach (ExportSheet sheet in sheets)
    {
      WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
      part.Worksheet = Worksheet(sheet);
      sheetList.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId++, Name = sheet.Name });
    }
  }

  /// <summary>A unique name Excel accepts: at most 31 characters, none of []:*?/\, no outer apostrophes, and never
  /// the reserved History.</summary>
  public static string SheetName(string title, HashSet<string> used)
  {
    string name = Regex.Replace(Regex.Replace(title, @"[\[\]:*?/\\]", " "), @"\s+", " ").Trim().Trim('\'').Trim();
    if (name.Length == 0)
    {
      name = "Sheet";
    }
    string candidate = Truncate(name, SheetNameLimit);
    for (int copy = 2; used.Contains(candidate) || candidate.Equals("History", StringComparison.OrdinalIgnoreCase);
      copy++)
    {
      string suffix = $" ({copy})";
      candidate = Truncate(name, SheetNameLimit - suffix.Length) + suffix;
    }
    used.Add(candidate);
    return candidate;
  }

  private static string Truncate(string name, int length)
  {
    return name.Length <= length ? name : name[..length].TrimEnd().TrimEnd('\'');
  }

  private static ExportSheet Layout(View view, string name, StyleTable styles)
  {
    var engine = new Engine(view);
    ViewPayload payload = PayloadBuilder.Build(view, engine, 1);
    var rows = new List<List<ExportCell?>> { new List<ExportCell?> { Text(payload.Title) } };
    if (payload.Subtitle.Length > 0)
    {
      rows.Add(new List<ExportCell?> { Text(payload.Subtitle) });
    }
    rows.Add(new List<ExportCell?>());

    // Text aligns left by default, so column labels would sit away from the figures below them.
    uint label = styles.Index(null, 0, true);
    var header = new List<ExportCell?> { null };
    header.AddRange(payload.Columns.Select(column => Text(column.Label, column.Label.Length, label)));
    rows.Add(header);

    foreach (RowPayload row in payload.Rows)
    {
      if (row.Kind == "spacer")
      {
        rows.Add(new List<ExportCell?>());
        continue;
      }
      var cells = new List<ExportCell?>
      {
        Text(row.Label, row.Label.Length + 2 * row.Indent, styles.Index(null, row.Indent))
      };
      cells.AddRange(row.Cells.Select(cell => Value(view, engine, cell, styles)));
      rows.Add(cells);
    }
    return new ExportSheet(name, rows);
  }

  private static ExportCell? Value(View view, Engine engine, CellPayload cell, StyleTable styles)
  {
    ResolvedCell resolved = engine.Cell(new CellCoordinate(cell.Line, cell.Column));
    if (resolved.Value is not double value)
    {
      string unresolved = view.Formatter.Unresolved;
      return unresolved.Length == 0 ? null : Text(unresolved, unresolved.Length);
    }
    string format = ExcelNumberFormat.For(view.Formatter.Spec(resolved.Units), PayloadBuilder.ZeroDisplay(view, resolved));
    // Negating a zero would write -0, which Excel shows in the negative section.
    double shown = cell.Contra && value != 0 ? -value : value;
    return new ExportCell(null, shown, styles.Index(format, 0), cell.Display.Length);
  }

  private static ExportCell Text(string text, int width = 0, uint style = 0)
  {
    return new ExportCell(text, null, style, width);
  }

  private static Worksheet Worksheet(ExportSheet sheet)
  {
    var data = new SheetData();
    for (int rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
    {
      var row = new Row { RowIndex = (uint)(rowIndex + 1) };
      for (int columnIndex = 0; columnIndex < sheet.Rows[rowIndex].Count; columnIndex++)
      {
        if (sheet.Rows[rowIndex][columnIndex] is ExportCell cell)
        {
          row.Append(Cell(cell, $"{ColumnName(columnIndex)}{rowIndex + 1}"));
        }
      }
      data.Append(row);
    }
    return new Worksheet(Columns(sheet), data);
  }

  private static Cell Cell(ExportCell cell, string reference)
  {
    var element = new Cell { CellReference = reference, StyleIndex = cell.Style == 0 ? null : cell.Style };
    if (cell.Number is double number)
    {
      element.CellValue = new CellValue(number.ToString("R", CultureInfo.InvariantCulture));
      return element;
    }
    element.DataType = CellValues.InlineString;
    element.InlineString = new InlineString(new Text(cell.Text!) { Space = SpaceProcessingModeValues.Preserve });
    return element;
  }

  /// <summary>Widths come from what each cell displays, measured in characters as Excel measures them.</summary>
  private static Columns Columns(ExportSheet sheet)
  {
    int count = sheet.Rows.Max(row => row.Count);
    var columns = new Columns();
    for (int index = 0; index < count; index++)
    {
      int widest = sheet.Rows.Where(row => index < row.Count).Max(row => row[index]?.Width ?? 0);
      columns.Append(new Column
      {
        Min = (uint)(index + 1),
        Max = (uint)(index + 1),
        Width = Math.Clamp(widest + 2, 10, 60),
        CustomWidth = true
      });
    }
    return columns;
  }

  private static string ColumnName(int index)
  {
    string name = "";
    for (int remaining = index + 1; remaining > 0; remaining = (remaining - 1) / 26)
    {
      name = (char)('A' + (remaining - 1) % 26) + name;
    }
    return name;
  }

  private sealed record ExportSheet(string Name, List<List<ExportCell?>> Rows);

  /// <summary>Either text or a number; <c>Width</c> is the characters it displays, which sizes its column.</summary>
  private sealed record ExportCell(string? Text, double? Number, uint Style, int Width);

  /// <summary>Cell formats by number format and alignment, registered as cells need them; index 0 is Excel's default.</summary>
  private sealed class StyleTable
  {
    private const uint FirstCustomFormat = 164;

    private readonly Dictionary<string, uint> numberFormats = new Dictionary<string, uint>();
    private readonly List<(uint NumberFormat, int Indent, bool Right)> cellFormats =
      new List<(uint, int, bool)> { (0, 0, false) };

    public uint Index(string? numberFormat, int indent, bool right = false)
    {
      uint formatId = 0;
      if (numberFormat is not null && !numberFormats.TryGetValue(numberFormat, out formatId))
      {
        formatId = FirstCustomFormat + (uint)numberFormats.Count;
        numberFormats.Add(numberFormat, formatId);
      }
      int index = cellFormats.IndexOf((formatId, indent, right));
      if (index < 0)
      {
        cellFormats.Add((formatId, indent, right));
        index = cellFormats.Count - 1;
      }
      return (uint)index;
    }

    public Stylesheet Stylesheet()
    {
      var stylesheet = new Stylesheet();
      if (numberFormats.Count > 0)
      {
        stylesheet.Append(new NumberingFormats(numberFormats.Select(format =>
          new NumberingFormat { NumberFormatId = format.Value, FormatCode = format.Key }))
        {
          Count = (uint)numberFormats.Count
        });
      }
      stylesheet.Append(
        new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 1 },
        new Fills(
          new Fill(new PatternFill { PatternType = PatternValues.None }),
          new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        {
          Count = 2
        },
        new Borders(new Border(
          new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()))
        {
          Count = 1
        },
        new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 })
        {
          Count = 1
        },
        new CellFormats(cellFormats.Select(CellFormat)) { Count = (uint)cellFormats.Count });
      return stylesheet;
    }

    private static CellFormat CellFormat((uint NumberFormat, int Indent, bool Right) format)
    {
      var cellFormat = new CellFormat
      {
        NumberFormatId = format.NumberFormat,
        FontId = 0,
        FillId = 0,
        BorderId = 0,
        FormatId = 0,
        ApplyNumberFormat = format.NumberFormat != 0
      };
      if (format.Indent > 0 || format.Right)
      {
        cellFormat.Alignment = new Alignment
        {
          Indent = format.Indent > 0 ? (uint)format.Indent : null,
          Horizontal = format.Right ? HorizontalAlignmentValues.Right : null
        };
        cellFormat.ApplyAlignment = true;
      }
      return cellFormat;
    }


  }


}