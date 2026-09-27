using Shouldly;
using Xunit;

namespace Libreta.Tests;

public class DateTests
{
  [Fact]
  public void DateArithmeticYieldsDatesAndDayCounts()
  {
    // Arrange
    using var fixture = new DateFixture("""
      a_month_later:
        formula: period_end + 31
        units: date
      days_elapsed:
        formula: period_end - prior(period_end)
      latest:
        formula: max(period_end["2025"], period_end["2026"])
        units: date
      """);
    var engine = new Engine(fixture.LoadView());

    // Act
    ResolvedCell later = engine.Cell("a_month_later", "2025");
    ResolvedCell elapsed = engine.Cell("days_elapsed", "2026");
    ResolvedCell latest = engine.Cell("latest", "2025");

    // Assert
    later.Value.ShouldBe(46053);
    later.Date.ShouldBe(true);
    elapsed.Value.ShouldBe(365);
    elapsed.Date.ShouldBe(false);
    latest.Value.ShouldBe(46387);
  }

  [Theory]
  [InlineData("2025", 12, "2026-12-31")]
  [InlineData("2025", 2, "2026-02-28")]
  [InlineData("2026", -10, "2026-02-28")]
  [InlineData("2025", 0, "2025-12-31")]
  [InlineData("2025", 1.9, "2026-01-31")]
  public void EndsTheMonthAWholeNumberOfMonthsAway(string period, double months, string expected)
  {
    // Arrange
    using var fixture = new DateFixture($"""
      month_end:
        formula: eomonth(period_end["{period}"], {months.ToString(System.Globalization.CultureInfo.InvariantCulture)})
        units: date
      """);
    var engine = new Engine(fixture.LoadView());
    Formatter.TryParseDate(expected, out double serial);

    // Act
    ResolvedCell cell = engine.Cell("month_end", "2025");

    // Assert
    cell.Value.ShouldBe(serial);
    cell.Date.ShouldBe(true);
  }

  [Theory]
  [InlineData("period_end * 2", "multiplies a date")]
  [InlineData("eomonth(revenue, 12)", "takes the eomonth of a number")]
  [InlineData("eomonth(period_end, period_end)", "moves eomonth by a date")]
  [InlineData("period_end / 2", "divides a date")]
  [InlineData("period_end ^ 2", "raises a date")]
  [InlineData("-period_end", "negates a date")]
  [InlineData("period_end + period_end", "adds two dates")]
  [InlineData("revenue - period_end", "subtracts a date from a number")]
  [InlineData("period_end > revenue", "compares a date with a number")]
  [InlineData("max(period_end, revenue)", "mixes dates and numbers")]
  [InlineData("sum(period_end[\"2025\":\"2026\"])", "sum of dates")]
  [InlineData("if(period_end > revenue, 1, 0)", "compares a date with a number")]
  public void RejectsArithmeticThatMisusesADate(string formula, string message)
  {
    // Arrange
    using var fixture = new DateFixture($"""
      misused:
        formula: {formula}
      """);
    var engine = new Engine(fixture.LoadView());

    // Act
    InvalidDataException exception = Should.Throw<InvalidDataException>(() => engine.Cell("misused", "2025"));

    // Assert
    exception.Message.ShouldContain("dates.misused[2025]");
    exception.Message.ShouldContain(message);
  }

  [Theory]
  [InlineData("period_end + 1", "", "holds a date but its units 'millions' have no date format")]
  [InlineData("revenue", "units: date", "holds a number but its units 'date' format a date")]
  public void RequiresDateUnitsExactlyWhereALineHoldsDates(string formula, string units, string message)
  {
    // Arrange
    using var fixture = new DateFixture($"""
      mislabeled:
        formula: {formula}
        {units}
      """);
    var engine = new Engine(fixture.LoadView());

    // Act
    InvalidDataException exception = Should.Throw<InvalidDataException>(() => engine.Cell("mislabeled", "2025"));

    // Assert
    exception.Message.ShouldContain(message);
  }

  [Fact]
  public void RequiresDateUnitsOnADateFact()
  {
    // Arrange
    using var fixture = new DateFixture("");
    fixture.Write("dates/facts/undated.csv", "line_item,2025,2026\nclosing,2025-12-31,2026-12-31\n");
    fixture.Write("dates/facts/undated.yaml", "table:\n  title: Undated\ndefaults:\n  units: millions\n");
    var engine = new Engine(fixture.LoadView());

    // Act
    InvalidDataException exception = Should.Throw<InvalidDataException>(() => engine.Cell("closing", "2025"));

    // Assert
    exception.Message.ShouldContain("'dates.closing[2025]' holds a date but its units 'millions' have no date format");
  }

  [Fact]
  public void RejectsAFactRowMixingDatesAndNumbers()
  {
    // Arrange
    using var fixture = new DateFixture("");
    fixture.Write("dates/facts/periods.csv", "line_item,2025,2026\nperiod_end,2025-12-31,46387\n");

    // Act
    InvalidDataException exception = Should.Throw<InvalidDataException>(() => fixture.LoadView());

    // Assert
    exception.Message.ShouldContain("mixes dates and numbers");
  }

  [Fact]
  public void RejectsAChecksMisuseOfADate()
  {
    // Arrange
    using var fixture = new DateFixture("");
    fixture.Write("dates/checks.yaml", "checks:\n  doubled_date:\n    formula: period_end * 2\n");
    var engine = new Engine(fixture.LoadView());

    // Act
    InvalidDataException exception = Should.Throw<InvalidDataException>(() => engine.RunChecks());

    // Assert
    exception.Message.ShouldContain("check 'doubled_date'");
    exception.Message.ShouldContain("multiplies a date");
  }

  private sealed class DateFixture : TempFolder
  {
    public DateFixture(string formulas)
    {
      Write("book.yaml", "name: Test\nshort_name: Test\nunits: millions\nnavigation: []\n");
      Write("formats.yaml", "formats:\n  millions:\n    decimals: 0\n  date:\n    date: yyyy-MM-dd\n");
      Write("dates/facts/periods.csv", "line_item,2025,2026\nperiod_end,2025-12-31,2026-12-31\n");
      Write("dates/facts/periods.yaml", "table:\n  title: Periods\ndefaults:\n  units: date\n");
      Write("dates/facts/inputs.csv", "line_item,2025,2026\nrevenue,100,120\n");
      Write("dates/facts/inputs.yaml", "table:\n  title: Inputs\ndefaults:\n  units: millions\n");
      Write("dates/formulas.yaml", formulas.Length == 0 ? "formulas: {}" : $"formulas:\n{Indent(formulas)}");
      Write("dates/view.yaml", "title: Dates\nrows:\n  - line: period_end\n");
    }

    public View LoadView()
    {
      return View.Load(Book.Load(Root), PathTo("dates"));
    }

    private static string Indent(string text)
    {
      return string.Join('\n', text.Split('\n').Select(line => $"  {line}"));
    }


  }


}