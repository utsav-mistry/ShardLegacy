using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShardLegacy.Server.Models
{
    /// <summary>
    /// Persisted record of an allocated host port, preventing reuse across restarts.
    /// </summary>
    public class PortRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public int PortNumber { get; set; }
        public string DeploymentId { get; set; } = string.Empty;
        public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
    }
}
