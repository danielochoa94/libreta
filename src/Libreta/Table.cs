namespace Libreta;

/// <summary>What a cell points at, whatever its position on the page: one book's line at one source column.</summary>
public readonly record struct CellCoordinate(string Line, string Column);

/// <summary>One entry on an axis, before orientation decides whether it is drawn as a row or a column.</summary>
public sealed class TableEntry
{
  public string Kind { get; init; } = "";
  public string Name { get; init; } = "";
  public string Label { get; init; } = "";
  public string Style { get; init; } = "";
  public int Indent { get; init; }
  public string? Sign { get; init; }
  public string? Note { get; init; }

  /// <summary>A metric entry's source column, per metric so a comparison can place one line at two periods.</summary>
  public string? Column { get; init; }
}

/// <summary>A view's selection resolved into coordinates, then oriented; the page and --view both read it.</summary>
public sealed class ResolvedTable
{
  private readonly List<TableEntry> _primary;
  private readonly List<TableEntry> _secondary;
  private readonly CellCoordinate?[,] _coordinates;

  private ResolvedTable(
    List<TableEntry> primary,
    List<TableEntry> secondary,
    CellCoordinate?[,] coordinates,
    bool transposed)
  {
    _primary = primary;
    _secondary = secondary;
    _coordinates = coordinates;
    Transposed = transposed;
  }

  public bool Transposed { get; }
  public List<TableEntry> Rows => Transposed ? _secondary : _primary;
  public List<TableEntry> Columns => Transposed ? _primary : _secondary;

  public CellCoordinate? Coordinate(int row, int column)
  {
    return Transposed ? _coordinates[column, row] : _coordinates[row, column];
  }

  /// <summary>The entry naming a cell's line, which owns line metadata such as sign: the row of a statement, the column
  /// of a comparison.</summary>
  public TableEntry LineEntry(int row, int column)
  {
    TableEntry rowEntry = Rows[row];
    return rowEntry.Kind == "line" ? rowEntry : Columns[column];
  }

  public IEnumerable<CellCoordinate> Coordinates()
  {
    for (int primary = 0; primary < _primary.Count; primary++)
    {
      for (int secondary = 0; secondary < _secondary.Count; secondary++)
      {
        if (_coordinates[primary, secondary] is CellCoordinate coordinate)
        {
          yield return coordinate;
        }
      }
    }
  }

  public static ResolvedTable Build(View view)
  {
    ViewFile presentation = view.Presentation;
    bool comparison = presentation.Rows.Any(row => row.Book is not null);
    List<TableEntry> primary = comparison ? Books(view) : Lines(view);
    List<TableEntry> secondary = comparison ? Metrics(view, primary) : SourceColumns(view);
    if (presentation.Transpose && primary.Any(entry => entry.Kind is "header" or "spacer"))
    {
      throw new InvalidDataException(
        "A transposed view cannot use header or spacer rows: neither survives the move to a column.");
    }

    var coordinates = new CellCoordinate?[primary.Count, secondary.Count];
    for (int row = 0; row < primary.Count; row++)
    {
      for (int column = 0; column < secondary.Count; column++)
      {
        coordinates[row, column] = comparison
          ? primary[row].Kind == "spacer" ? null : Coordinate(view, primary[row], secondary[column])
          : primary[row].Kind == "line" ? new CellCoordinate(primary[row].Name, secondary[column].Name) : null;
      }
    }
    return new ResolvedTable(primary, secondary, coordinates, presentation.Transpose);
  }

  private static CellCoordinate Coordinate(View view, TableEntry row, TableEntry metric)
  {
    string name = BookStructure.Normalize(metric.Name);
    // A summary row already names a folder inside this book, so its metric hangs off that folder directly;
    // a book row resolves the metric inside the included book the way that book's own views would.
    string line = row.Kind == "scope"
      ? $"{row.Name}.{name}"
      : view.Book.Resolve(name, row.Name) ?? BookStructure.Qualify(row.Name, name);
    string column = view.Book.CoordinateColumn(metric.Column!, row.Name, line);
    return new CellCoordinate(line, column);
  }

  private static List<TableEntry> Lines(View view)
  {
    var entries = new List<TableEntry>();
    foreach (ViewRow row in view.Presentation.Rows)
    {
      if (row.Space is not null)
      {
        entries.Add(new TableEntry { Kind = "spacer", Style = row.Space });
        continue;
      }
      if (row.Line is null)
      {
        entries.Add(new TableEntry { Kind = "header", Label = row.Label ?? "" });
        continue;
      }
      string name = view.Resolve(row.Line) ?? row.Line;
      entries.Add(new TableEntry
      {
        Kind = "line",
        Name = name,
        Label = row.Label ?? view.LabelOf(name),
        Style = row.Style ?? "",
        Indent = row.Indent,
        Sign = row.Sign
      });
    }
    return entries;
  }

  private static List<TableEntry> SourceColumns(View view)
  {
    ViewColumn? metric = view.Presentation.Columns.FirstOrDefault(column => column.Line is not null);
    if (metric is not null)
    {
      throw new InvalidDataException(
        $"Column '{metric.Line}' names a line, which only a view whose rows name books can select.");
    }
    if (view.Presentation.Columns.Count == 0)
    {
      return view.Periods
        .Select(column => new TableEntry
        {
          Kind = "column",
          Name = view.ResolveColumn(column),
          Label = view.ColumnLabel(column)
        })
        .ToList();
    }

    var entries = new List<TableEntry>();
    foreach (ViewColumn column in view.Presentation.Columns)
    {
      if (column.Source is null)
      {
        throw new InvalidDataException("A statement column mapping must name its source column.");
      }
      string name = view.ResolveColumn(column.Source);
      view.ColumnFormulas.TryGetValue(name, out ColumnFormula? formula);
      entries.Add(new TableEntry
      {
        Kind = "column",
        Name = name,
        Label = column.Label ?? view.ColumnLabel(column.Source),
        Style = column.Style ?? "",
        Note = column.Note?.Trim() ?? formula?.Note
      });
    }
    return entries;
  }

  private static List<TableEntry> Books(View view)
  {
    var entries = new List<TableEntry>();
    foreach (ViewRow row in view.Presentation.Rows)
    {
      if (row.Space is not null)
      {
        entries.Add(new TableEntry { Kind = "spacer", Style = row.Space });
        continue;
      }
      if (row.Scope is not null)
      {
        string scope = view.Book.ResolveScope(row.Scope, view.Scope)
          ?? throw new InvalidDataException($"Row references an unknown folder: {row.Scope}");
        entries.Add(new TableEntry
        {
          Kind = "scope",
          Name = scope,
          Label = row.Label ?? view.LabelOf(scope),
          Style = row.Style ?? "",
          Indent = row.Indent
        });
        continue;
      }
      if (row.Book is null)
      {
        throw new InvalidDataException(
          "A view comparing books lists one book per row: every row must name a book or a folder summarizing them.");
      }
      BookDefinition definition = view.Book.ResolveBook(row.Book, view.Scope)
        ?? throw new InvalidDataException($"Row references an unknown included book: {row.Book}");
      entries.Add(new TableEntry
      {
        Kind = "book",
        Name = definition.Namespace,
        Label = row.Label ?? definition.File.ShortName,
        Style = row.Style ?? "",
        Indent = row.Indent
      });
    }
    return entries;
  }

  private static List<TableEntry> Metrics(View view, List<TableEntry> books)
  {
    var entries = new List<TableEntry>();
    foreach (ViewColumn column in view.Presentation.Columns)
    {
      if (column.Line is null)
      {
        throw new InvalidDataException(
          $"Column '{column.Source}' must name a line: a view comparing books selects one metric per column.");
      }
      if (column.Column is null)
      {
        throw new InvalidDataException(
          $"Column '{column.Line}' must name a column to read its line at; periods are never inferred.");
      }
      string name = BookStructure.Normalize(column.Line);
      entries.Add(new TableEntry
      {
        Kind = "line",
        Name = name,
        Label = column.Label ?? MetricLabel(view, books, name),
        Style = column.Style ?? "",
        Sign = column.Sign,
        Note = column.Note?.Trim(),
        Column = column.Column
      });
    }
    return entries;
  }

  private static string MetricLabel(View view, List<TableEntry> books, string name)
  {
    return books
      .Select(book => view.Book.Resolve(name, book.Name))
      .Where(line => line is not null)
      .Select(line => view.LabelOf(line!))
      .FirstOrDefault() ?? name;
  }


}