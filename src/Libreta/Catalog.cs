namespace Libreta;

public record ViewCatalogEntry(string Id, string Title);

public record ViewCatalog(
  string Name,
  string ShortName,
  List<ViewCatalogEntry> Views,
  long Version,
  string? Error = null);

/// <summary>The navigation a loaded book presents, validated against the views it owns.</summary>
public static class CatalogLoader
{
  public static ViewCatalog Load(Book book, long version)
  {
    BookStructure structure = book.Structure;
    var viewsByBook = new Dictionary<BookDefinition, Dictionary<string, ViewCatalogEntry>>();
    foreach (BookDefinition definition in structure.Definitions)
    {
      var discovered = new Dictionary<string, ViewCatalogEntry>();
      foreach (string path in structure.Files(definition, "view.yaml"))
      {
        (string localId, string id) = BookStructure.ViewId(definition, Path.GetDirectoryName(path)!);
        string title = book.Presentations[id].Title;
        discovered.Add(localId, new ViewCatalogEntry(id, title.Length > 0 ? title : Label(localId.Split('/')[^1])));
      }
      viewsByBook.Add(definition, discovered);
      ValidateLocalNavigation(definition, discovered);
    }

    var views = new List<ViewCatalogEntry>();
    AddNavigation(structure.Definition, structure.Definition.File.Navigation, viewsByBook, views);
    return new ViewCatalog(
      structure.Definition.File.Name,
      structure.Definition.File.ShortName,
      views,
      version);
  }

  private static void ValidateLocalNavigation(
    BookDefinition definition,
    Dictionary<string, ViewCatalogEntry> discovered)
  {
    var included = new HashSet<string>();
    ValidateLocalItems(definition, definition.File.Navigation, null, discovered, included);
    string[] omitted = discovered.Keys.Except(included).OrderBy(id => id).ToArray();
    if (omitted.Length > 0)
    {
      throw new InvalidDataException(
        $"'{Path.Combine(definition.Root, "book.yaml")}' navigation omits: {string.Join(", ", omitted)}");
    }
  }

  private static void ValidateLocalItems(
    BookDefinition definition,
    List<NavigationItem> items,
    string? parent,
    Dictionary<string, ViewCatalogEntry> discovered,
    HashSet<string> included)
  {
    foreach (NavigationItem item in items)
    {
      if (item.Book.Length > 0)
      {
        if (!definition.Children.Any(child => child.Id == item.Book))
        {
          throw new InvalidDataException($"Navigation references unknown included book: {item.Book}");
        }
        continue;
      }
      if (parent is not null && !item.View.StartsWith($"{parent}/", StringComparison.Ordinal))
      {
        throw new InvalidDataException($"Navigation view '{item.View}' is not nested beneath '{parent}'");
      }
      if (!discovered.ContainsKey(item.View))
      {
        throw new InvalidDataException($"Navigation references unknown view: {item.View}");
      }
      if (!included.Add(item.View))
      {
        throw new InvalidDataException($"Navigation repeats view: {item.View}");
      }
      ValidateLocalItems(definition, item.Children, item.View, discovered, included);
    }
  }

  private static void AddNavigation(
    BookDefinition definition,
    List<NavigationItem> items,
    Dictionary<BookDefinition, Dictionary<string, ViewCatalogEntry>> viewsByBook,
    List<ViewCatalogEntry> views)
  {
    foreach (NavigationItem item in items)
    {
      if (item.Book.Length > 0)
      {
        BookDefinition child = definition.Children.Single(candidate => candidate.Id == item.Book);
        AddNavigation(child, child.File.Navigation, viewsByBook, views);
        continue;
      }
      views.Add(viewsByBook[definition][item.View]);
      AddNavigation(definition, item.Children, viewsByBook, views);
    }
  }

  private static string Label(string segment)
  {
    return string.Join(' ', segment.Split('-').Select(word =>
      word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
  }


}