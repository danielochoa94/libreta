using System.Text.Json;
using Shouldly;
using Xunit;

namespace Libreta.Tests;

[Collection(ConsoleCollection.Name)]
public class FormulaInspectionTests
{
  [Fact]
  public void PayloadCarriesEachUnitsScaleAndSymbols()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();
    fixture.Append("formats.yaml", "  basis_points:\n    scale: 10000\n    prefix: \"~\"\n    suffix: \" bp\"\n");

    // Act
    ViewPayload payload = fixture.Payload("statement");

    // Assert
    payload.Units["basis_points"].ShouldBe(new UnitPayload(10000, "~", " bp"));
    payload.Units["percent"].ShouldBe(new UnitPayload(100, "", "%"));
    payload.Units[""].ShouldBe(new UnitPayload(1, "", ""));
    payload.Units.ShouldNotContainKey("date");
  }

  [Fact]
  public void EachWrittenReferenceKeepsItsOwnColumn()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();
    fixture.Append("statement/formulas.yaml", """
        growth:
          formula: revenue["2025"] - revenue["2024"]
      """);
    fixture.Append("statement/view.yaml", "  - line: growth\n");

    // Act
    ViewPayload payload = fixture.Payload("statement");

    // Assert
    FormulaPayload formula = payload.Rows[3].Cells[1].Formula!;
    formula.References.Count.ShouldBe(2);
    formula.References.Select(reference => reference.Column).ShouldBe(new[] { "2025", "2024" });
    formula.References.Select(reference => reference.Line)
      .ShouldAllBe(line => line == "statement.revenue");
    Written(formula, 0).ShouldBe("revenue[\"2025\"]");
    Written(formula, 1).ShouldBe("revenue[\"2024\"]");
  }

  [Fact]
  public void TransposingLeavesWrittenReferencesAtTheSameCoordinates()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();
    fixture.Append("statement/formulas.yaml", """
        growth:
          formula: revenue["2025"] - revenue["2024"]
      """);
    fixture.Append("statement/view.yaml", "  - line: growth\n");
    ViewPayload upright = fixture.Payload("statement");
    fixture.Append("statement/view.yaml", "transpose: true\n");

    // Act
    ViewPayload transposed = fixture.Payload("statement");

    // Assert
    transposed.Rows[1].Cells[3].Formula!.References.ShouldBe(upright.Rows[3].Cells[1].Formula!.References);
  }

  [Fact]
  public void PriorReadsThePrecedingColumnOfItsOwnReference()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();
    fixture.Append("statement/formulas.yaml", """
        growth:
          formula: revenue - prior(revenue)
      """);
    fixture.Append("statement/view.yaml", "  - line: growth\n");

    // Act
    ViewPayload payload = fixture.Payload("statement");

    // Assert
    FormulaPayload formula = payload.Rows[3].Cells[1].Formula!;
    formula.References.Select(reference => reference.Column).ShouldBe(new[] { "2025", "2024" });
  }

  [Fact]
  public void AWholeLineFormulaLinksToLinesWhereNoColumnIsWritten()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();
    fixture.Append("statement/formulas.yaml", """
        growth:
          formula: revenue["2025"] - cost_of_revenue
      """);
    fixture.Append("statement/view.yaml", "  - line: growth\n");

    // Act
    ViewPayload payload = fixture.Payload("statement");

    // Assert
    FormulaPayload formula = payload.Rows[3].Formula!;
    formula.References.Select(reference => reference.Column).ShouldBe(new string?[] { "2025", null });
    formula.References.Select(reference => reference.Line)
      .ShouldBe(new[] { "statement.revenue", "statement.cost_of_revenue" });
  }

  [Fact]
  public void ADateReadsAsADateInItsCellAndInTheCalculationsThatUseIt()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();
    fixture.Append("formats.yaml", "  date:\n    date: MMM d, yyyy\n  integer:\n    decimals: 0\n");
    fixture.Write("statement/facts/dates.csv", "line_item,2024,2025\nperiod_end,2024-12-31,2025-12-31\n");
    fixture.Write("statement/facts/dates.yaml", "table:\n  title: Dates\ndefaults:\n  units: date\n");
    fixture.Append("statement/formulas.yaml", """
        days_in_period:
          formula: period_end - prior(period_end)
          units: integer
      """);
    fixture.Append("statement/view.yaml", "  - line: period_end\n  - line: days_in_period\n");

    // Act
    ViewPayload payload = fixture.Payload("statement");

    // Assert
    CellPayload periodEnd = payload.Rows[3].Cells[1];
    periodEnd.Display.ShouldBe("Dec 31, 2025");
    periodEnd.Exact.ShouldBe("2025-12-31");
    CellPayload days = payload.Rows[4].Cells[1];
    days.Display.ShouldBe("365");
    days.Calculation!.Tokens.Select(token => token.Text).ShouldBe(new[] { "Dec 31, 2025", " − ", "Dec 31, 2024" });
  }

  [Fact]
  public void AWildcardBookReferenceStandsForEveryIncludedBook()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison();
    fixture.WriteSpread("median(books::metrics.revenue[\"2025\"])");

    // Act
    ViewPayload payload = fixture.Payload("summary");

    // Assert
    payload.Rows[0].Cells[0].Exact.ShouldBe("200");
    FormulaPayload formula = payload.Rows[0].Cells[0].Formula!;
    formula.Text.ShouldBe("median(alpha::metrics.revenue[\"2025\"], beta::metrics.revenue[\"2025\"])");
    formula.References.Select(reference => reference.Line)
      .ShouldBe(new[] { "alpha::metrics.revenue", "beta::metrics.revenue" });
    formula.References.Select(reference => formula.Text.Substring(reference.Start, reference.Length))
      .ShouldBe(new[] { "alpha::metrics.revenue[\"2025\"]", "beta::metrics.revenue[\"2025\"]" });
  }

  [Theory]
  [InlineData("books::metrics.revenue[\"2025\"] * 2", "only sum, min, max, average, or median")]
  [InlineData("sum(books::metrics.revenue[\"2025\"] * 2)", "only sum, min, max, average, or median")]
  public void RejectsAWildcardBookReferenceOutsideAnAggregate(string formula, string expected)
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison();
    fixture.WriteSpread(formula);

    // Act
    InvalidDataException exception = Should.Throw<InvalidDataException>(() => fixture.Payload("summary"));

    // Assert
    exception.Message.ShouldContain(expected);
  }

  [Fact]
  public void QualifiedReferencesKeepTheBookTheyName()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison();
    fixture.WriteSpread("alpha::metrics.revenue - beta::metrics.revenue");

    // Act
    ViewPayload payload = fixture.Payload("summary");

    // Assert
    FormulaPayload formula = payload.Rows[0].Cells[0].Formula!;
    formula.References.Select(reference => reference.Line)
      .ShouldBe(new[] { "alpha::metrics.revenue", "beta::metrics.revenue" });
    Written(formula, 0).ShouldBe("alpha::metrics.revenue");
    Written(formula, 1).ShouldBe("beta::metrics.revenue");
    formula.References.Select(reference => reference.SourceView)
      .ShouldBe(new[] { "alpha/metrics", "beta/metrics" });
  }

  [Fact]
  public void ADependencyTheTableOmitsAtThatColumnStaysInspectable()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison("""
      columns:
        - line: metrics.doubled
          column: double
          label: Doubled
      """);

    // Act
    ViewPayload payload = fixture.Payload("comps");

    // Assert
    CellPayload cell = payload.Rows[0].Cells[0];
    cell.Exact.ShouldBe("200");
    CalculationTokenPayload token = cell.Calculation!.Tokens
      .Single(candidate => candidate.Dependency == "alpha::metrics.doubled");
    token.Column.ShouldBe("2025");
    token.ExpansionId.ShouldNotBeNull();
    payload.Calculations.ShouldContainKey(token.ExpansionId!);
    cell.Formula!.References.Single().FormulaId.ShouldBe(token.ExpansionId);
  }

  [Fact]
  public void ADependencyDisplayedAtItsOwnCoordinateIsSelectedRatherThanExpanded()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison("""
      columns:
        - line: metrics.revenue
          column: "2025"
          label: Revenue
        - line: metrics.doubled
          column: "2025"
          label: Doubled
      """);

    // Act
    ViewPayload payload = fixture.Payload("comps");

    // Assert
    CellPayload cell = payload.Rows[0].Cells[1];
    CalculationTokenPayload token = cell.Calculation!.Tokens
      .Single(candidate => candidate.Dependency == "alpha::metrics.revenue");
    token.ExpansionId.ShouldBeNull();
    token.Expansion.ShouldBeNull();
    cell.Formula!.References.Single().FormulaId.ShouldBeNull();
  }

  [Fact]
  public void ACalculationValueCarriesTheLabelOfItsLine()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();

    // Act
    ViewPayload payload = fixture.Payload("statement");

    // Assert
    CalculationTokenPayload token = payload.Rows[2].Cells[1].Calculation!.Tokens
      .First(candidate => candidate.Dependency is not null);
    token.Label.ShouldBe("Revenue");
  }

  [Fact]
  public void AShortColumnNameResolvesInTheBookOfTheSelectedLine()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison();

    // Act
    (int exitCode, string output) = fixture.Value("comps", "alpha::metrics.doubled", "double");
    (int canonicalExit, string canonical) = fixture.Value("comps", "alpha::metrics.doubled", "alpha::metrics.double");

    // Assert
    exitCode.ShouldBe(0);
    output.Trim().ShouldBe("200");
    canonicalExit.ShouldBe(0);
    canonical.Trim().ShouldBe("200");
  }

  [Fact]
  public void TwoBooksAnswerTheSameShortColumnNameWithTheirOwnCalculation()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison();

    // Act
    (int exitCode, string output) = fixture.Value("comps", "beta::metrics.doubled", "double");

    // Assert
    exitCode.ShouldBe(0);
    output.Trim().ShouldBe("600");
  }

  [Fact]
  public void ARangeCarriesTheCalculationOfEveryCellItSweepsUp()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteStatement();
    fixture.Append("statement/formulas.yaml", """
        two_year:
          formula: sum(gross_profit["2024":"2025"])
      """);
    fixture.Write("statement/view.yaml", """
      title: Statement
      columns: [2025]
      rows:
        - line: two_year
      """);

    // Act
    ViewPayload payload = fixture.Payload("statement");

    // Assert
    List<CalculationTokenPayload> members = payload.Rows[0].Cells[0].Calculation!.Tokens
      .Where(token => token.Dependency == "statement.gross_profit").ToList();
    members.Select(token => token.Column).ShouldBe(new[] { "2024", "2025" });
    members.Select(token => token.ExpansionId).ShouldAllBe(id => id != null);
    foreach (CalculationTokenPayload member in members)
    {
      payload.Calculations[member.ExpansionId!].Tokens
        .ShouldContain(token => token.Dependency == "statement.revenue");
    }
  }

  [Fact]
  public void AValueAnsweredOnItsOwnCarriesTheFormulasItsReferencesName()
  {
    // Arrange
    using var fixture = new ViewFixture();
    fixture.WriteComparison("""
      columns:
        - line: metrics.doubled
          column: double
          label: Doubled
      """);

    // Act
    (int exitCode, string output) = fixture.Value("comps", "alpha::metrics.doubled", "double", true);

    // Assert
    exitCode.ShouldBe(0);
    using JsonDocument document = JsonDocument.Parse(output);
    document.RootElement.GetProperty("inputs")[0].GetProperty("formula").GetString().ShouldBe("revenue * 2");
  }

  private static string Written(FormulaPayload formula, int index)
  {
    FormulaReferencePayload reference = formula.References[index];
    return formula.Text.Substring(reference.Start, reference.Length);
  }


}
