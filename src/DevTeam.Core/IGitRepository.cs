namespace DevTeam.Core;

public interface IGitRepository
{
    bool IsGitRepository(string workingDirectory);
    bool EnsureRepository(string workingDirectory);
    GitStatusSnapshot? TryCaptureStatus(string workingDirectory);
    IReadOnlyList<string> StagePathsChangedSince(string workingDirectory, GitStatusSnapshot? beforeSnapshot);
}
