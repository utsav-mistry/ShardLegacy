using MongoDB.Driver;
using ShardLegacy.Server.Models;

namespace ShardLegacy.Server.Services
{
    /// <summary>
    /// Provides typed MongoDB collection access and a lazy connection health check.
    /// All collections are created with sensible indexes on startup.
    /// </summary>
    public class MongoDbService
    {
        private readonly IMongoDatabase? _db;
        private readonly ILogger<MongoDbService> _logger;
        private bool _indexesEnsured = false;

        public MongoDbService(IConfiguration config, ILogger<MongoDbService> logger)
        {
            _logger = logger;

            var connStr = config.GetConnectionString("MongoDB")
                ?? config.GetValue<string>("MongoDB:ConnectionString")
                ?? "mongodb://localhost:27017";

            var dbName = config.GetValue<string>("MongoDB:DatabaseName") ?? "shardlegacy";

            try
            {
                var client = new MongoClient(connStr);
                _db = client.GetDatabase(dbName);
                _logger.LogInformation("MongoDB configured: {Db} @ {Conn}", dbName, connStr);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MongoDB client configuration failed; continuing without persistence.");
                _db = null;
            }
        }

        public IMongoCollection<ProjectDeployment>? Deployments =>
            _db?.GetCollection<ProjectDeployment>("deployments");

        public IMongoCollection<PortRecord>? Ports =>
            _db?.GetCollection<PortRecord>("ports");

        /// <summary>
        /// Ensures indexes exist. Call once at startup.
        /// </summary>
        public async Task EnsureIndexesAsync()
        {
            if (_indexesEnsured || _db == null) return;

            try
            {
                // Deployments: index on deployedAt descending for quick list queries
                var depIdxModel = new CreateIndexModel<ProjectDeployment>(
                    Builders<ProjectDeployment>.IndexKeys.Descending(d => d.DeployedAt),
                    new CreateIndexOptions { Name = "deployedAt_desc" });
                await Deployments!.Indexes.CreateOneAsync(depIdxModel);

                // Ports: unique index on portNumber
                var portIdxModel = new CreateIndexModel<PortRecord>(
                    Builders<PortRecord>.IndexKeys.Ascending(p => p.PortNumber),
                    new CreateIndexOptions { Unique = true, Name = "port_unique" });
                await Ports!.Indexes.CreateOneAsync(portIdxModel);

                _indexesEnsured = true;
                _logger.LogInformation("MongoDB indexes ensured.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not ensure MongoDB indexes (may already exist).");
            }
        }

        /// <summary>
        /// Checks whether MongoDB is reachable.
        /// </summary>
        public bool IsConnected => _db != null;

        public async Task<bool> PingAsync()
        {
            if (_db == null) return false;

            try
            {
                await _db.RunCommandAsync((Command<MongoDB.Bson.BsonDocument>)"{ping:1}");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
