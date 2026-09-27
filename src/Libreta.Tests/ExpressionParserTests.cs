using System.Globalization;
using Shouldly;
using Xunit;

namespace Libreta.Tests;

public class ExpressionParserTests
{
  [Fact]
  public void ParsesExponentiationAsRightAssociative()
  {
    // Arrange
    const string source = "2 ^ 3 ^ 2";

    // Act
    BinaryExpr expression = ExpressionParser.Parse(source).ShouldBeOfType<BinaryExpr>();

    // Assert
    expression.Op.ShouldBe('^');
    expression.Left.ShouldBeOfType<NumberExpr>();
    expression.Right.ShouldBeOfType<BinaryExpr>().Op.ShouldBe('^');
  }

  [Fact]
  public void ExponentiationBindsMoreTightlyThanUnaryMinus()
  {
    // Arrange
    const string source = "-2 ^ 2";

    // Act
    UnaryExpr expression = ExpressionParser.Parse(source).ShouldBeOfType<UnaryExpr>();

    // Assert
    expression.Operand.ShouldBeOfType<BinaryExpr>().Op.ShouldBe('^');
  }

  [Fact]
  public void ParsesExplicitColumnReference()
  {
    // Arrange
    const string source = "revenue[\"FY 2025\"]";

    // Act
    RefExpr expression = ExpressionParser.Parse(source).ShouldBeOfType<RefExpr>();

    // Assert
    expression.Name.ShouldBe("revenue");
    expression.Column.ShouldBe("FY 2025");
  }

  [Fact]
  public void ParsesAnInclusiveRange()
  {
    // Arrange
    const string source = "present_value[\"2026E\":\"2030E\"]";

    // Act
    RangeExpr expression = ExpressionParser.Parse(source).ShouldBeOfType<RangeExpr>();

    // Assert
    expression.Name.ShouldBe("present_value");
    expression.StartColumn.ShouldBe("2026E");
    expression.EndColumn.ShouldBe("2030E");
  }

  [Fact]
  public void ParsesAConditionalComparison()
  {
    // Arrange
    const string source = "if(ebit >= 0, max(ebit, 0), min(ebit, 0))";

    // Act
    FunctionExpr expression = ExpressionParser.Parse(source).ShouldBeOfType<FunctionExpr>();

    // Assert
    expression.Name.ShouldBe("if");
    expression.Arguments.Count.ShouldBe(3);
    expression.Arguments[0].ShouldBeOfType<ComparisonExpr>().Op.ShouldBe(">=");
    expression.Arguments[1].ShouldBeOfType<FunctionExpr>().Name.ShouldBe("max");
    expression.Arguments[2].ShouldBeOfType<FunctionExpr>().Name.ShouldBe("min");
  }

  [Fact]
  public void ParsesAnAggregateOverARange()
  {
    // Arrange
    const string source = "max(revenue[\"2023\":\"2025\"])";

    // Act
    FunctionExpr expression = ExpressionParser.Parse(source).ShouldBeOfType<FunctionExpr>();

    // Assert
    expression.Name.ShouldBe("max");
    expression.Arguments.Single().ShouldBeOfType<RangeExpr>();
  }

  [Theory]
  [InlineData("average")]
  [InlineData("median")]
  public void ParsesStatisticalAggregates(string function)
  {
    // Arrange
    string source = $"{function}(revenue[\"2023\":\"2025\"], 90)";

    // Act
    FunctionExpr expression = ExpressionParser.Parse(source).ShouldBeOfType<FunctionExpr>();

    // Assert
    expression.Name.ShouldBe(function);
    expression.Arguments.Count.ShouldBe(2);
    expression.Arguments[0].ShouldBeOfType<RangeExpr>();
    expression.Arguments[1].ShouldBeOfType<NumberExpr>();
  }

  [Fact]
  public void ParsesNumbersUsingInvariantCulture()
  {
    // Arrange
    CultureInfo originalCulture = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

    try
    {
      // Act
      NumberExpr expression = ExpressionParser.Parse("1.5").ShouldBeOfType<NumberExpr>();

      // Assert
      expression.Value.ShouldBe(1.5);
    }
    finally
    {
      CultureInfo.CurrentCulture = originalCulture;
    }
  }


}