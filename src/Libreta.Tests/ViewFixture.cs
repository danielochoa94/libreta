namespace Libreta.Tests;

/// <summary>A synthetic book on disk: a statement, or a parent whose two child books hold the same lines,
/// which is what a test needs before it can ask how a view presents them.</summary>
public sealed class ViewFixture : TempFolder
{
  public ViewFixture()
  {
    Write("formats.yaml", "formats:\n  millions:\n    decimals: 0\n");
  }

  public void WriteStatement()
  {
    Write("book.yaml", "name: Statement book\nshort_name: STMT\nnavigation: [statement]\n");
    Write("statement/facts/income.csv", "line_item,2024,2025\nrevenue,90,120\ncost_of_revenue,-40,-50\n");
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
      """);
    Write("statement/formulas.yaml", "formulas:\n  gross_profit:\n    formula: revenue - cost_of_revenue\n");
    Write("statement/view.yaml", """
      title: Statement
      columns: [2024, 2025]
      rows:
        - line: revenue
        - line: cost_of_revenue
        - line: gross_profit
          label: Gross profit
      """);
  }

  public void WriteComparison(string? columns = null)
  {
    Write("book.yaml", """
      name: Comparison book
      short_name: COMPS
      navigation:
        - comps
        - book: alpha
        - book: beta
      books:
        - id: alpha
          path: children/alpha
        - id: beta
          path: children/beta
      """);
    WriteChild("alpha", "Alpha book", "ALPHA", 90, 100);
    WriteChild("beta", "Beta book", "BETA", 250, 300);
    Write("comps/view.yaml", $$"""
      title: Comparison
      rows:
        - book: alpha
          label: Alpha
        - book: beta
      {{columns ?? """
      columns:
        - line: metrics.revenue
          column: "2025"
          label: Revenue
        - line: metrics.doubled
          column: double
          label: Doubled
      """}}
      """);
  }

  /// <summary>A view in the parent book whose line reaches into both children, which is the only place a
  /// reference written with a book qualifier can be rendered.</summary>
  public void WriteSpread(string formula)
  {
    Write("summary/formulas.yaml", $"formulas:\n  spread:\n    formula: {formula}\n");
    Write("summary/view.yaml", """
      title: Summary
      columns: ["2025"]
      rows:
        - line: spread
      """);
  }

  public ViewPayload Payload(string viewId)
  {
    Book book = Book.Load(Root);
    Libreta.View view = Libreta.View.Load(book, Path.Combine(Root, viewId.Replace('/', Path.DirectorySeparatorChar)));
    return PayloadBuilder.Build(view, new Engine(view), 1);
  }

  public (int ExitCode, string Output) Value(string viewId, string line, string column, bool json = false)
  {
    return Run(HeadlessCommand.Value, new ViewQuery(viewId, line, column), json);
  }

  public (int ExitCode, string Output) Table(string viewId)
  {
    return Run(HeadlessCommand.View, new ViewQuery(viewId));
  }

  public (int ExitCode, string Output) Check()
  {
    return Run(HeadlessCommand.Check, null);
  }

  public void Append(string relative, string content)
  {
    File.AppendAllText(PathTo(relative), $"{content}\n");
  }

  private void WriteChild(string id, string name, string shortName, double prior, double current)
  {
    Write($"children/{id}/book.yaml", $"name: {name}\nshort_name: {shortName}\nnavigation: [metrics]\n");
    Write($"children/{id}/metrics/facts/figures.csv", $"line_item,2024,2025\nrevenue,{prior},{current}\n");
    Write($"children/{id}/metrics/facts/figures.yaml", """
      table:
        title: Figures
      defaults:
        units: millions
      line_items:
        revenue:
          label: Revenue
      """);
    Write($"children/{id}/metrics/formulas.yaml", """
      formulas:
        doubled:
          formula: revenue * 2
      column_formulas:
        double:
          label: Double
          formula: self["2025"]
      """);
    Write($"children/{id}/metrics/view.yaml", """
      title: Metrics
      columns: [2024, 2025]
      rows:
        - line: revenue
        - line: doubled
          label: Doubled
      """);
  }

  private (int ExitCode, string Output) Run(HeadlessCommand command, ViewQuery? query, bool json = false)
  {
    TextWriter original = Console.Out;
    using var writer = new StringWriter();
    try
    {
      Console.SetOut(writer);
      int exitCode = HeadlessRunner.Run(Root, command, query, json);
      return (exitCode, writer.ToString());
    }
    finally
    {
      Console.SetOut(original);
    }
  }


}