namespace DevTeam.Core;

public interface IFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string content);
    void DeleteFile(string path);
    void MoveFile(string source, string dest);
    void ReplaceFile(string source, string dest);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void DeleteDirectory(string path, bool recursive);
    string[] GetFiles(string directory, string searchPattern);
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption);
    void SetFileAttributes(string path, FileAttributes attributes);
}
