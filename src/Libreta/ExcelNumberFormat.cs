using System.Text;

namespace Libreta;

/// <summary>A formats.yaml spec as an Excel number format, so a cell holds its exact value and only displays the
/// scaled, rounded and decorated form.</summary>
public static class ExcelNumberFormat
{
  /// <summary><paramref name="zero"/> replaces a zero outright, as a calculated zero reads as a dash.</summary>
  public static string For(FormatSpec spec, string? zero)
  {
    if (spec.Date is not null)
    {
      return DatePattern(spec.Date);
    }

    string suffix = spec.Suffix;
    bool percent = spec.Scale == 100 && suffix.EndsWith('%');
    if (percent)
    {
      suffix = suffix[..^1];
    }
    int thousands = percent ? 0 : Thousands(spec.Scale);

    string digits = (spec.Separator ? "#,##0" : "0") +
      (spec.Decimals > 0 ? "." + new string('0', spec.Decimals) : "") +
      new string(',', thousands);
    string positive = Literal(spec.Prefix) + digits + Literal(suffix) + (percent ? "%" : "");
    string negative = spec.Negative == "parentheses" ? $"({positive})" : $"-{positive}";
    return zero is null ? $"{positive};{negative}" : $"{positive};{negative};{Literal(zero)}";
  }

  /// <summary>Excel divides by a thousand for each comma after the digits, and scales no other way.</summary>
  private static int Thousands(double scale)
  {
    for (int commas = 0; commas <= 4; commas++)
    {
      if (Math.Abs(scale * Math.Pow(1000, commas) - 1) < 1e-9)
      {
        return commas;
      }
    }
    throw new InvalidDataException(
      $"A format with scale {scale} cannot be written to Excel, which scales only by thousands or to a percent.");
  }

  /// <summary>Translates .NET date specifiers, which match Excel's apart from Excel writing months in lower case.</summary>
  private static string DatePattern(string pattern)
  {
    var format = new StringBuilder();
    for (int index = 0; index < pattern.Length; index++)
    {
      char symbol = pattern[index];
      if (symbol is 'y' or 'M' or 'd')
      {
        int run = 1;
        while (index + run < pattern.Length && pattern[index + run] == symbol)
        {
          run++;
        }
        format.Append(char.ToLowerInvariant(symbol), symbol == 'M' ? Math.Min(run, 4) : run);
        index += run - 1;
      }
      else if (symbol is '\'' or '"')
      {
        int end = pattern.IndexOf(symbol, index + 1);
        if (end < 0)
        {
          throw new InvalidDataException($"Date pattern '{pattern}' leaves a quote open.");
        }
        format.Append(Literal(pattern[(index + 1)..end]));
        index = end;
      }
      else if (symbol == '\\' && index + 1 < pattern.Length)
      {
        format.Append(Literal(pattern[++index].ToString()));
      }
      else if (char.IsLetter(symbol))
      {
        throw new InvalidDataException(
          $"Date pattern '{pattern}' uses '{symbol}', which a date without a time cannot show in Excel.");
      }
      else
      {
        format.Append(symbol is ' ' or '-' or '/' or ',' or '.' or ':' or '(' or ')' ? symbol.ToString()
          : Literal(symbol.ToString()));
      }
    }
    return format.ToString();
  }

  private static string Literal(string text)
  {
    return text.Length == 0 ? "" : $"\"{text.Replace("\"", "\"\\\"\"")}\"";
  }


}