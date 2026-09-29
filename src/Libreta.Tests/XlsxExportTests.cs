using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shouldly;
using Xunit;

namespace Libreta.Tests;

public class XlsxExportTests
{
  [Fact]
  public void WritesOneSheetPerViewInNavigationOrder()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    workbook.Worksheets.Select(sheet => sheet.Name).ShouldBe(new[] { "Statement", "Valuation", "Supporting" });
  }

  [Fact]
  public void WritesAWorkbookTheSchemaAccepts()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();
    var stream = new MemoryStream();
    XlsxExport.Write(fixture.Root, stream);

    // Act
    using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false);
    IEnumerable<string> errors = new OpenXmlValidator().Validate(document)
      .Select(error => $"{error.Path?.XPath}: {error.Description}");

    // Assert
    errors.ShouldBeEmpty();
  }

  [Fact]
  public void HeadsTheSheetWithTheTitleAndColumnLabels()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLWorksheet sheet = workbook.Worksheet("Statement");
    sheet.Cell("A1").GetString().ShouldBe("Statement");
    sheet.Cell("A2").GetString().ShouldBe("In millions");
    IXLRow header = HeaderRow(sheet);
    header.Cell(2).GetString().ShouldBe("2024");
    header.Cell(3).GetString().ShouldBe("FY 2025");
  }

  [Fact]
  public void AlignsColumnLabelsWithTheirValues()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLRow header = HeaderRow(workbook.Worksheet("Statement"));
    header.Cell(2).Style.Alignment.Horizontal.ShouldBe(XLAlignmentHorizontalValues.Right);
    header.Cell(3).Style.Alignment.Horizontal.ShouldBe(XLAlignmentHorizontalValues.Right);
  }

  [Fact]
  public void HoldsTheValuesThePageShows()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLWorksheet sheet = workbook.Worksheet("Statement");
    Values(Row(sheet, "Revenue")).ShouldBe(new double?[] { 90, 120 });
    Values(Row(sheet, "Cost of revenue")).ShouldBe(new double?[] { -40, -50 });
    Values(Row(sheet, "Gross profit")).ShouldBe(new double?[] { 50, 70 });
  }

  [Fact]
  public void LeavesAnUnresolvedCellEmpty()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLRow growth = Row(workbook.Worksheet("Statement"), "Growth");
    growth.Cell(2).IsEmpty().ShouldBeTrue();
    growth.Cell(3).GetDouble().ShouldBe(120.0 / 90 - 1, 1e-12);
  }

  [Fact]
  public void FormatsCellsByTheirUnits()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLWorksheet sheet = workbook.Worksheet("Statement");
    Row(sheet, "Revenue").Cell(2).Style.NumberFormat.Format.ShouldBe("#,##0;(#,##0)");
    Row(sheet, "Growth").Cell(3).Style.NumberFormat.Format.ShouldBe("#,##0.0%;(#,##0.0%);\"-\"");
  }

  [Fact]
  public void ShowsACalculatedZeroAsTheZeroText()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLCell unchanged = Row(workbook.Worksheet("Statement"), "Unchanged").Cell(2);
    unchanged.GetDouble().ShouldBe(0);
    unchanged.GetFormattedString().ShouldBe("-");
  }

  [Fact]
  public void ShowsAPrintedDashAsTheDash()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLCell dash = Row(workbook.Worksheet("Statement"), "Other income").Cell(2);
    dash.GetDouble().ShouldBe(0);
    dash.Style.NumberFormat.Format.ShouldBe("#,##0;(#,##0);\"-\"");
  }

  [Fact]
  public void KeepsHeadersSpacersAndIndents()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLWorksheet sheet = workbook.Worksheet("Statement");
    IXLRow operations = Row(sheet, "Operations");
    operations.Cell(2).IsEmpty().ShouldBeTrue();
    Row(sheet, "Cost of revenue").Cell(1).Style.Alignment.Indent.ShouldBe(1);
    int gross = Row(sheet, "Gross profit").RowNumber();
    sheet.Row(gross + 1).IsEmpty().ShouldBeTrue();
    Row(sheet, "Growth").RowNumber().ShouldBe(gross + 2);
  }

  [Fact]
  public void WritesDatesAsSerialsWithADateFormat()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLCell date = Row(workbook.Worksheet("Valuation"), "Valuation date").Cell(2);
    date.Style.NumberFormat.Format.ShouldBe("mmm d, yyyy");
    date.GetDateTime().ShouldBe(new DateTime(2026, 6, 30));
  }

  [Fact]
  public void LeavesFactsAsValues()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    Row(workbook.Worksheet("Statement"), "Revenue").Cell(2).HasFormula.ShouldBeFalse();
  }

  [Fact]
  public void WritesFormulasOverTheCellsThePageShows()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLWorksheet sheet = workbook.Worksheet("Statement");
    Row(sheet, "Gross profit").Cell(2).FormulaA1.ShouldBe("B6+B7");
    Row(sheet, "Growth").Cell(3).FormulaA1.ShouldBe("C6/B6-1");
  }

  [Fact]
  public void ReadsAnotherViewOnItsSheet()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLWorksheet sheet = workbook.Worksheet("Valuation");
    Row(sheet, "Total cost").Cell(2).FormulaA1.ShouldBe("-SUM('Statement'!B7:C7)");
    Row(sheet, "Year end").Cell(2).FormulaA1.ShouldBe("EOMONTH(B4,6)");
    Row(sheet, "Capped value").Cell(2).FormulaA1.ShouldBe("IF(B7>500,500,B7)");
  }

  [Fact]
  public void ShowsLinesNoViewPresentsOnASupportingSheet()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLRow multiple = Row(workbook.Worksheet("Supporting"), "Multiple");
    multiple.Cell(2).GetDouble().ShouldBe(8);
    Row(workbook.Worksheet("Valuation"), "Equity value").Cell(2).FormulaA1
      .ShouldBe($"'Statement'!C8*'Supporting'!B{multiple.RowNumber()}");
  }

  [Fact]
  public void RecalculatesEveryFormulaToTheValueThePageShows()
  {
    // Arrange
    using ExportFixture fixture = new ExportFixture();
    using XLWorkbook workbook = fixture.Export();
    List<IXLCell> formulas = workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed(cell => cell.HasFormula)).ToList();

    // Act
    List<(string Cell, double Shown, double Recalculated)> results = formulas
      .Select(cell => (cell.Address.ToString(XLReferenceStyle.A1, true), Number(cell.CachedValue), Recalculate(cell)))
      .ToList();

    // Assert
    formulas.Count.ShouldBe(9);
    results.ShouldAllBe(result => Math.Abs(result.Shown - result.Recalculated) < 1e-9);
  }

  [Theory]
  [InlineData("a - b", false, "B4+B5", 1)]
  [InlineData("a + b", false, "B4-B5", 5)]
  [InlineData("a - b", true, "-B4-B5", -1)]
  [InlineData("b - a", true, "B5+B4", 1)]
  [InlineData("a - (a - b)", false, "B4-(B4+B5)", 2)]
  [InlineData("a / (b + 1)", false, "B4/(1-B5)", 1)]
  [InlineData("a + b / 2", false, "B4-B5/2", 4)]
  [InlineData("a * b", false, "-B4*B5", 6)]
  [InlineData("a * b", true, "B4*B5", -6)]
  [InlineData("-a ^ 2", false, "-(B4^2)", -9)]
  [InlineData("b ^ 2", false, "(-B5)^2", 4)]
  [InlineData("a > b", false, "IF(B4>-B5,1,0)", 1)]
  [InlineData("if(a > b, a, b)", false, "IF(B4>-B5,B4,-B5)", 3)]
  [InlineData("max(a, b) / 2", false, "MAX(B4,-B5)/2", 1.5)]
  public void WritesFormulasInThePageSign(string formula, bool contra, string expected, double shown)
  {
    // Arrange
    using FormulaFixture fixture = new FormulaFixture(formula, contra);

    // Act
    using XLWorkbook workbook = fixture.Export();

    // Assert
    IXLCell result = Row(workbook.Worksheet("Model"), "Result").Cell(2);
    result.FormulaA1.ShouldBe(expected);
    result.GetDouble().ShouldBe(shown);
    Recalculate(result).ShouldBe(shown, 1e-12);
  }

  [Theory]
  [InlineData("Cash flow: detail", "Cash flow detail")]
  [InlineData("A/B [draft]?", "A B draft")]
  [InlineData("'Quoted'", "Quoted")]
  [InlineData("   ", "Sheet")]
  [InlineData("History", "History (2)")]
  [InlineData("A title far longer than Excel allows a sheet", "A title far longer than Excel a")]
  public void NamesSheetsTheWayExcelAllows(string title, string expected)
  {
    // Arrange
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Act
    string name = XlsxExport.SheetName(title, used);

    // Assert
    name.ShouldBe(expected);
  }

  [Fact]
  public void NumbersRepeatedSheetNames()
  {
    // Arrange
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    string first = XlsxExport.SheetName("Segments", used);

    // Act
    string second = XlsxExport.SheetName("segments", used);

    // Assert
    first.ShouldBe("Segments");
    second.ShouldBe("segments (2)");
  }

  private static IXLRow HeaderRow(IXLWorksheet sheet)
  {
    return sheet.RowsUsed().First(row => row.Cell(2).GetString() == "2024");
  }

  private static IXLRow Row(IXLWorksheet sheet, string label)
  {
    return sheet.RowsUsed().Single(row => row.Cell(1).GetString() == label);
  }

  private static double?[] Values(IXLRow row)
  {
    return new[] { row.Cell(2), row.Cell(3) }.Select(cell => cell.IsEmpty() ? (double?)null : cell.GetDouble())
      .ToArray();
  }

  /// <summary>Evaluates the formula afresh, reading its inputs as the workbook holds them.</summary>
  private static double Recalculate(IXLCell cell)
  {
    return Number(cell.Worksheet.Evaluate(cell.FormulaA1, cell.Address.ToString()));
  }

  private static double Number(XLCellValue value)
  {
    return value.IsDateTime ? value.GetDateTime().ToOADate() : value.GetNumber();
  }

  private sealed class FormulaFixture : TempFolder
  {
    public FormulaFixture(string formula, bool contra)
    {
      Write("formats.yaml", "formats:\n  millions:\n    decimals: 1\n");
      Write("book.yaml", "name: Formula book\nshort_name: FOR\nnavigation: [model]\n");
      Write("model/facts/inputs.csv", "line_item,2025\na,3\nb,2\n");
      Write("model/facts/inputs.yaml", """
        table:
          title: Inputs
        defaults:
          units: millions
        line_items:
          b:
            sign: contra
        """);
      Write("model/formulas.yaml", $"formulas:\n  result:\n    formula: {formula}\n    units: millions\n");
      Write("model/view.yaml", $"""
        title: Model
        columns: ["2025"]
        rows:
          - line: a
          - line: b
          - line: result
            sign: {(contra ? "contra" : "additive")}
        """);
    }

    public XLWorkbook Export()
    {
      var stream = new MemoryStream();
      XlsxExport.Write(Root, stream);
      stream.Position = 0;
      return new XLWorkbook(stream);
    }
  }

  private sealed class ExportFixture : TempFolder
  {
    public ExportFixture()
    {
      Write("formats.yaml", "formats:\n  millions:\n    decimals: 0\n  multiple:\n    decimals: 1\n    suffix: x\n");
      Write("book.yaml", "name: Export book\nshort_name: EXP\nnavigation: [statement, valuation]\n");
      Write("statement/facts/income.csv",
        "line_item,2024,2025\nrevenue,90,120\ncost_of_revenue,-40,-50\nother_income,-,3\n");
      Write("statement/facts/income.yaml", """
        table:
          title: Income statement
        defaults:
          printed: negated
          units: millions
        line_items:
          revenue:
            label: Revenue
            printed: as_is
          cost_of_revenue:
            label: Cost of revenue
            sign: contra
          other_income:
            label: Other income
            printed: as_is
        """);
      Write("statement/formulas.yaml", """
        formulas:
          gross_profit:
            formula: revenue - cost_of_revenue
            units: millions
          growth:
            formula: revenue / prior(revenue) - 1
            units: percent
          unchanged:
            formula: revenue - revenue
            units: millions
        """);
      Write("statement/view.yaml", """
        title: Statement
        subtitle: In millions
        columns:
          - "2024"
          - source: "2025"
            label: FY 2025
        rows:
          - label: Operations
          - line: revenue
          - line: cost_of_revenue
            indent: 1
          - line: gross_profit
            label: Gross profit
          - space: normal
          - line: growth
          - line: unchanged
          - line: other_income
        """);
      Write("valuation/facts/inputs.csv", "line_item,Value\nvaluation_date,2026-06-30\nmultiple,8\n");
      Write("valuation/facts/inputs.yaml", """
        table:
          title: Inputs
        defaults:
          units: date
        line_items:
          valuation_date:
            label: Valuation date
          multiple:
            units: multiple
        """);
      Write("valuation/formulas.yaml", """
        formulas:
          year_end:
            formula: eomonth(valuation_date, 6)
            units: date
          total_cost:
            formula: sum(statement.cost_of_revenue["2024":"2025"])
            units: millions
          equity_value:
            formula: statement.gross_profit["2025"] * multiple
            units: millions
          capped_value:
            formula: if(equity_value > 500, 500, equity_value)
            units: millions
        """);
      Write("valuation/view.yaml", """
        title: Valuation
        columns: [Value]
        rows:
          - line: valuation_date
          - line: year_end
          - line: total_cost
          - line: equity_value
          - line: capped_value
        """);
    }

    public XLWorkbook Export()
    {
      var stream = new MemoryStream();
      XlsxExport.Write(Root, stream);
      stream.Position = 0;
      return new XLWorkbook(stream);
    }
  }


}