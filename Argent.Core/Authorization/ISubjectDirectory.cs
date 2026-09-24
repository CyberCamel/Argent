namespace Argent.Core.Authorization;

public interface ISubjectDirectory
{
    Task<IReadOnlyList<SubjectEntry>> GetSubjectsAsync();
}

public record SubjectEntry(string Id, string Label, string Icon, bool IsGroup, bool IsRole = false);
