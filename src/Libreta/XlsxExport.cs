using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Libreta;

/// <summary>The whole book as one workbook: a sheet per view in navigation order, each cell holding the value the page
/// shows, or the formula that calculates it, and formatted by its units.</summary>
public static class XlsxExport
{
  private const int SheetNameLimit = 31;

  public static void Write(string root, Stream output)
  {
    Book book = Book.Load(root);
    List<ViewCatalogEntry> entries = CatalogLoader.Load(book, 1).Views;
    var builder = new Builder(book, entries.Select(entry => book.Views[entry.Id]).ToList());
    List<ExportSheet> sheets = entries.Select(entry => builder.Layout(book.Views[entry.Id], entry.Title)).ToList();
    if (builder.Supporting() is ExportSheet supporting)
    {
      sheets.Add(supporting);
    }
    builder.WriteFormulas();

    using SpreadsheetDocument document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
    WorkbookPart workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();
    workbookPart.AddNewPart<WorkbookStylesPart>().Stylesheet = builder.Styles.Stylesheet();
    Sheets sheetList = workbookPart.Workbook.AppendChild(new Sheets());
    uint sheetId = 1;
    foreach (ExportSheet sheet in sheets)
    {
      WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
      part.Worksheet = Worksheet(sheet);
      sheetList.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId++, Name = sheet.Name });
    }
    // The cached values are the engine's; Excel recalculates on opening rather than trusting them.
    workbookPart.Workbook.Append(new CalculationProperties { FullCalculationOnLoad = true });
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
          row.Append(Cell(cell, new ExcelAddress(sheet.Name, rowIndex, columnIndex, false).Cell));
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
      if (cell.Formula is not null)
      {
        element.CellFormula = new CellFormula(cell.Formula);
      }
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

  private sealed record ExportSheet(string Name, List<List<ExportCell?>> Rows);

  /// <summary>Either text or a number, which a formula may calculate; <c>Width</c> is the characters it displays,
  /// which sizes its column.</summary>
  private sealed record ExportCell(string? Text, double? Number, uint Style, int Width, string? Formula = null);

  /// <summary>A calculated cell, written once every cell it reads has an address.</summary>
  private sealed record PendingFormula(ExportSheet Sheet, int Row, int Column, ResolvedCell Cell, bool Negated);

  /// <summary>Lays out the sheets in two passes: every shown cell takes an address first, so a formula can read a
  /// cell on any sheet, including one laid out after its own.</summary>
  private sealed class Builder
  {
    private readonly Book book;
    private readonly List<View> views;
    private readonly HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CellCoordinate, List<ExcelAddress>> addresses =
      new Dictionary<CellCoordinate, List<ExcelAddress>>();
    private readonly List<PendingFormula> formulas = new List<PendingFormula>();

    public Builder(Book book, List<View> views)
    {
      this.book = book;
      this.views = views;
    }

    public StyleTable Styles { get; } = new StyleTable();

    public ExportSheet Layout(View view, string title)
    {
      var engine = new Engine(view);
      ViewPayload payload = PayloadBuilder.Build(view, engine, 1);
      var sheet = new ExportSheet(SheetName(title, names),
        new List<List<ExportCell?>> { new List<ExportCell?> { Text(payload.Title) } });
      if (payload.Subtitle.Length > 0)
      {
        sheet.Rows.Add(new List<ExportCell?> { Text(payload.Subtitle) });
      }
      sheet.Rows.Add(new List<ExportCell?>());
      sheet.Rows.Add(Header(null, payload.Columns.Select(column => column.Label)));

      foreach (RowPayload row in payload.Rows)
      {
        if (row.Kind == "spacer")
        {
          sheet.Rows.Add(new List<ExportCell?>());
          continue;
        }
        var cells = new List<ExportCell?>
        {
          Text(row.Label, row.Label.Length + 2 * row.Indent, Styles.Index(null, row.Indent))
        };
        sheet.Rows.Add(cells);
        foreach (CellPayload cell in row.Cells)
        {
          cells.Add(Place(sheet, cells.Count, view, engine.Cell(new CellCoordinate(cell.Line, cell.Column)),
            cell.Contra));
        }
      }
      return sheet;
    }

    /// <summary>The cells formulas read that no view shows, such as an intermediate line, grouped by the folder that
    /// defines them.</summary>
    public ExportSheet? Supporting()
    {
      var engine = new Engine(views[0]);
      var seen = new HashSet<CellCoordinate>(addresses.Keys);
      var needed = new List<(CellCoordinate Coordinate, ResolvedCell? Cell)>();
      var pending = new Queue<CellCoordinate>(formulas.SelectMany(formula => Reads(formula.Cell)));
      while (pending.TryDequeue(out CellCoordinate coordinate))
      {
        if (!seen.Add(coordinate))
        {
          continue;
        }
        ResolvedCell? cell = Resolve(engine, coordinate);
        needed.Add((coordinate, cell));
        if (cell?.Value is not null && cell.Expression is not null)
        {
          foreach (CellCoordinate read in Reads(cell))
          {
            pending.Enqueue(read);
          }
        }
      }
      if (needed.Count == 0)
      {
        return null;
      }

      var sheet = new ExportSheet(SheetName("Supporting", names), new List<List<ExportCell?>>
      {
        new List<ExportCell?> { Text("Supporting") },
        new List<ExportCell?> { Text("Lines the views read but do not show") }
      });
      foreach (IGrouping<string, (CellCoordinate Coordinate, ResolvedCell? Cell)> block in
        needed.GroupBy(item => LineScope(item.Coordinate.Line)))
      {
        List<string> periods = book.PeriodsFor(block.Key);
        List<string> columns = block.Select(item => item.Coordinate.Column).Distinct()
          .OrderBy(column => periods.IndexOf(column) is int index and >= 0 ? index : int.MaxValue)
          .ToList();
        View view = views.FirstOrDefault(candidate => candidate.Scope == block.Key) ?? views[0];
        sheet.Rows.Add(new List<ExportCell?>());
        sheet.Rows.Add(Header(BlockTitle(block.Key), columns.Select(ColumnTitle)));
        foreach (IGrouping<string, (CellCoordinate Coordinate, ResolvedCell? Cell)> line in
          block.GroupBy(item => item.Coordinate.Line))
        {
          string label = view.LabelOf(line.Key);
          var cells = new List<ExportCell?> { Text(label, label.Length) };
          cells.AddRange(Enumerable.Repeat<ExportCell?>(null, columns.Count));
          sheet.Rows.Add(cells);
          foreach ((CellCoordinate coordinate, ResolvedCell? cell) in line)
          {
            int column = columns.IndexOf(coordinate.Column) + 1;
            cells[column] = cell is null
              ? null
              : Place(sheet, column, Formatting(view, cell.Units), cell, false);
          }
        }
      }
      return sheet;
    }

    public void WriteFormulas()
    {
      var writer = new ExcelFormula(book, addresses);
      foreach (PendingFormula formula in formulas)
      {
        ResolvedCell cell = formula.Cell;
        List<ExportCell?> row = formula.Sheet.Rows[formula.Row];
        row[formula.Column] = row[formula.Column]! with
        {
          Formula = writer.Write(cell.Expression!, cell.Column, cell.Scope!, cell.Line, formula.Sheet.Name,
            formula.Negated)
        };
      }
    }

    /// <summary>A cell at the end of the sheet's last row, taking its address and any formula it calculates.</summary>
    private ExportCell? Place(ExportSheet sheet, int column, View view, ResolvedCell cell, bool contra)
    {
      int row = sheet.Rows.Count - 1;
      var coordinate = new CellCoordinate(cell.Line, cell.Column);
      if (!addresses.TryGetValue(coordinate, out List<ExcelAddress>? shown))
      {
        shown = new List<ExcelAddress>();
        addresses.Add(coordinate, shown);
      }
      shown.Add(new ExcelAddress(sheet.Name, row, column, contra));

      if (cell.Value is not double value)
      {
        string unresolved = view.Formatter.Unresolved;
        return unresolved.Length == 0 ? null : Text(unresolved, unresolved.Length);
      }
      if (cell.Expression is not null)
      {
        formulas.Add(new PendingFormula(sheet, row, column, cell, contra));
      }
      string format = ExcelNumberFormat.For(view.Formatter.Spec(cell.Units), PayloadBuilder.ZeroDisplay(view, cell));
      // Negating a zero would write -0, which Excel shows in the negative section.
      double shownValue = contra && value != 0 ? -value : value;
      int width = PayloadBuilder.Display(view, cell, contra).Length;
      return new ExportCell(null, shownValue, Styles.Index(format, 0), width);
    }

    private List<ExportCell?> Header(string? title, IEnumerable<string> labels)
    {
      // Text aligns left by default, so column labels would sit away from the figures below them.
      uint style = Styles.Index(null, 0, true);
      var header = new List<ExportCell?> { title is null ? null : Text(title, title.Length) };
      header.AddRange(labels.Select(label => Text(label, label.Length, style)));
      return header;
    }

    private IEnumerable<CellCoordinate> Reads(ResolvedCell cell)
    {
      return ExcelFormula.Reads(book, cell.Expression!, cell.Column, cell.Scope!, cell.Line);
    }

    /// <summary>A cell only a branch the condition never takes reads may not resolve at all; it stays empty.</summary>
    private static ResolvedCell? Resolve(Engine engine, CellCoordinate coordinate)
    {
      try
      {
        return engine.Cell(coordinate);
      }
      catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
      {
        return null;
      }
    }

    private string LineScope(string line)
    {
      return book.Formulas.TryGetValue(line, out Formula? formula)
        ? formula.Scope
        : book.Facts.TryGetValue(line, out Fact? fact) ? fact.Scope : "";
    }

    private string BlockTitle(string scope)
    {
      return book.ViewIdsByScope.TryGetValue(scope, out string? id) && book.Presentations[id].Title.Length > 0
        ? book.Presentations[id].Title
        : scope.Length > 0 ? scope : book.Structure.Definition.File.Name;
    }

    private string ColumnTitle(string column)
    {
      return book.ColumnFormulas.TryGetValue(column, out ColumnFormula? formula) && formula.Label.Length > 0
        ? formula.Label
        : column;
    }

    /// <summary>The view that formats a supporting cell: the one over its folder, else the first that knows its
    /// units.</summary>
    private View Formatting(View preferred, string? units)
    {
      return preferred.Formatter.Defines(units)
        ? preferred
        : views.FirstOrDefault(view => view.Formatter.Defines(units)) ?? preferred;
    }


  }

  /// <summary>Cell formats by number format and alignment, registered as cells need them; index 0 is Excel's
  /// default.</summary>
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