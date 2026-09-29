using Shouldly;
using Xunit;

namespace Libreta.Tests;

public class ExcelNumberFormatTests
{
  [Fact]
  public void WritesSeparatedDigitsWithParenthesizedNegatives()
  {
    // Arrange
    var spec = new FormatSpec { Decimals = 0 };

    // Act
    string format = ExcelNumberFormat.For(spec, null);

    // Assert
    format.ShouldBe("#,##0;(#,##0)");
  }

  [Fact]
  public void LetsExcelScaleAPercent()
  {
    // Arrange
    var spec = new FormatSpec { Scale = 100, Decimals = 1, Suffix = "%" };

    // Act
    string format = ExcelNumberFormat.For(spec, null);

    // Assert
    format.ShouldBe("#,##0.0%;(#,##0.0%)");
  }

  [Fact]
  public void QuotesAPercentSignThatDoesNotScale()
  {
    // Arrange
    var spec = new FormatSpec { Decimals = 1, Suffix = "%" };

    // Act
    string format = ExcelNumberFormat.For(spec, null);

    // Assert
    format.ShouldBe("#,##0.0\"%\";(#,##0.0\"%\")");
  }

  [Fact]
  public void QuotesPrefixesAndSuffixesAndPutsTheMinusBeforeThePrefix()
  {
    // Arrange
    var spec = new FormatSpec { Decimals = 2, Separator = false, Prefix = "$", Suffix = "x", Negative = "minus" };

    // Act
    string format = ExcelNumberFormat.For(spec, null);

    // Assert
    format.ShouldBe("\"$\"0.00\"x\";-\"$\"0.00\"x\"");
  }

  [Fact]
  public void ShowsZeroAsTheGivenText()
  {
    // Arrange
    var spec = new FormatSpec { Decimals = 0 };

    // Act
    string format = ExcelNumberFormat.For(spec, "-");

    // Assert
    format.ShouldBe("#,##0;(#,##0);\"-\"");
  }

  [Theory]
  [InlineData(0.001, "#,##0,;(#,##0,)")]
  [InlineData(0.000001, "#,##0,,;(#,##0,,)")]
  public void ScalesByThousandsWithTrailingCommas(double scale, string expected)
  {
    // Arrange
    var spec = new FormatSpec { Scale = scale, Decimals = 0 };

    // Act
    string format = ExcelNumberFormat.For(spec, null);

    // Assert
    format.ShouldBe(expected);
  }

  [Fact]
  public void RefusesAScaleExcelCannotShow()
  {
    // Arrange
    var spec = new FormatSpec { Scale = 2 };

    // Act, Assert
    Should.Throw<InvalidDataException>(() => ExcelNumberFormat.For(spec, null)).Message.ShouldContain("scale");
  }

  [Theory]
  [InlineData("MMM d, yyyy", "mmm d, yyyy")]
  [InlineData("yyyy-MM-dd", "yyyy-mm-dd")]
  [InlineData("dddd d MMMM yy", "dddd d mmmm yy")]
  [InlineData("'FY'yyyy", "\"FY\"yyyy")]
  public void TranslatesDatePatterns(string pattern, string expected)
  {
    // Arrange
    var spec = new FormatSpec { Date = pattern };

    // Act
    string format = ExcelNumberFormat.For(spec, "-");

    // Assert
    format.ShouldBe(expected);
  }

  [Fact]
  public void RefusesATimeInADatePattern()
  {
    // Arrange
    var spec = new FormatSpec { Date = "yyyy-MM-dd HH:mm" };

    // Act, Assert
    Should.Throw<InvalidDataException>(() => ExcelNumberFormat.For(spec, null));
  }


}