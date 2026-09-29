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
    workbook.Worksheets.Select(sheet => sheet.Name).ShouldBe(new[] { "Statement", "Valuation" });
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

  private sealed class ExportFixture : TempFolder
  {
    public ExportFixture()
    {
      Write("formats.yaml", "formats:\n  millions:\n    decimals: 0\n");
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
      Write("valuation/facts/inputs.csv", "line_item,Value\nvaluation_date,2026-06-30\n");
      Write("valuation/facts/inputs.yaml", """
        table:
          title: Inputs
        defaults:
          units: date
        line_items:
          valuation_date:
            label: Valuation date
        """);
      Write("valuation/view.yaml", "title: Valuation\ncolumns: [Value]\nrows:\n  - line: valuation_date\n");
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