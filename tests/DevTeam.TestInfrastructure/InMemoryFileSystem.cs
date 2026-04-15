namespace DevTeam.TestInfrastructure;

public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => _files.ContainsKey(path);
    public string ReadAllText(string path) => _files.TryGetValue(path, out var c) ? c
        : throw new FileNotFoundException($"File not found: {path}");
    public void WriteAllText(string path, string content) { _files[path] = content; }
    public void DeleteFile(string path) => _files.Remove(path);
    public void MoveFile(string source, string dest) { _files[dest] = _files[source]; _files.Remove(source); }
    public void ReplaceFile(string source, string dest) { _files[dest] = _files[source]; _files.Remove(source); }
    public bool DirectoryExists(string path) => _directories.Contains(path);
    public void CreateDirectory(string path) => _directories.Add(path);
    public void DeleteDirectory(string path, bool recursive)
    {
        _directories.Remove(path);
        if (recursive)
        {
            foreach (var k in _files.Keys.Where(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList())
                _files.Remove(k);
        }
    }
    public string[] GetFiles(string directory, string searchPattern) =>
        _files.Keys.Where(k => k.StartsWith(directory, StringComparison.OrdinalIgnoreCase)).ToArray();
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption) =>
        GetFiles(directory, searchPattern);
    public void SetFileAttributes(string path, FileAttributes attributes) { }

    public IReadOnlyDictionary<string, string> Files => _files;
}
