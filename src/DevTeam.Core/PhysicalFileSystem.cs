using System.Text;

namespace DevTeam.Core;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string content) => File.WriteAllText(path, content, Encoding.UTF8);
    public void DeleteFile(string path) => File.Delete(path);
    public void MoveFile(string source, string dest) => File.Move(source, dest);
    public void ReplaceFile(string source, string dest) => File.Replace(source, dest, null, ignoreMetadataErrors: true);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public string[] GetFiles(string directory, string searchPattern) => Directory.GetFiles(directory, searchPattern);
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption) =>
        Directory.EnumerateFiles(directory, searchPattern, searchOption);
    public void SetFileAttributes(string path, FileAttributes attributes) => File.SetAttributes(path, attributes);
}
