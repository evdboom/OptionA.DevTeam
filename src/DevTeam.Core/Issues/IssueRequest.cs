namespace DevTeam.Core;
public class IssueRequest
{       public required string Title { get ; set; }
        public required string Detail { get ; set; }
        public required string RoleSlug { get ; set; }
        public int Priority { get ; set; }
        public int? RoadmapItemId { get ; set; }
        public IEnumerable<int> DependsOn { get ; set; } = [];
        public string? Area { get ; set; }
        public string? FamilyKey { get ; set; }
        public int? ParentIssueId { get ; set; }
        public int? PipelineId { get ; set; }
        public int? PipelineStageIndex { get ; set; }
        public int? ComplexityHint { get ; set; }
}