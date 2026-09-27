using Xunit;

namespace Libreta.Tests;

/// <summary>Console.Out is one object for the whole process: a class that captures it sees whatever another
/// class logs at the same moment. Everything that reads or writes it belongs here rather than in parallel.</summary>
[CollectionDefinition(Name)]
public class ConsoleCollection
{
  public const string Name = "console output";
}