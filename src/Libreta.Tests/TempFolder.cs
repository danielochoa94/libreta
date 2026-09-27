namespace Libreta.Tests;

/// <summary>A fresh folder under the system temp directory, deleted on dispose.</summary>
public class TempFolder : IDisposable
{
  public TempFolder()
  {
    Root = Path.Combine(Path.GetTempPath(), "libreta-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Root);
  }

  public string Root { get; }

  public string PathTo(string relative)
  {
    return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
  }

  public void Write(string relative, string content)
  {
    string path = PathTo(relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content.EndsWith('\n') ? content : $"{content}\n");
  }

  public void WriteBytes(string relative, byte[] content)
  {
    string path = PathTo(relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, content);
  }

  public void Dispose()
  {
    Directory.Delete(Root, true);
  }


}