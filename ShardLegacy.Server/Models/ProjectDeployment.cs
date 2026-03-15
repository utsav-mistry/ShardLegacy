using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShardLegacy.Server.Models
{
    public class ProjectDeployment
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string DetectedFile { get; set; } = string.Empty;
        public string ContainerId { get; set; } = string.Empty;
        public string ImageName { get; set; } = string.Empty;
        public int AssignedPort { get; set; }
        public int InternalPort { get; set; }
        public string Subdomain { get; set; } = string.Empty;
        public string FullUrl { get; set; } = string.Empty;
        public string DirectUrl { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public DateTime DeployedAt { get; set; }

        [BsonRepresentation(BsonType.String)]
        public DateTime? CompletedAt { get; set; }

        public long DurationMs { get; set; }
        public List<DeploymentStage> Stages { get; set; } = new();
        public List<LogEntry> Logs { get; set; } = new();
        public List<EnvVariable> EnvironmentVariables { get; set; } = new();
    }

    public class DeploymentStage
    {
        public int Order { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Status { get; set; } = "pending"; // pending, running, completed, failed, skipped

        [BsonRepresentation(BsonType.String)]
        public DateTime? StartedAt { get; set; }

        [BsonRepresentation(BsonType.String)]
        public DateTime? CompletedAt { get; set; }

        public long DurationMs { get; set; }
    }

    public class LogEntry
    {
        [BsonRepresentation(BsonType.String)]
        public DateTime Timestamp { get; set; }

        public string Level { get; set; } = "info"; // info, warn, error, success, step
        public string Message { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
    }

    public class EnvVariable
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsSecret { get; set; }
    }

    public class ScanResult
    {
        public bool Success { get; set; }
        public string LocalPath { get; set; } = string.Empty;
        public List<string> DetectedFiles { get; set; } = new();
        public string ProjectName { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string DetectedFramework { get; set; } = string.Empty;
    }

    public class DeployRequest
    {
        public string Source { get; set; } = string.Empty;
        public string? ProjectName { get; set; }
        public string? SelectedFile { get; set; }
        public List<EnvVariable>? EnvironmentVariables { get; set; }
    }
}
