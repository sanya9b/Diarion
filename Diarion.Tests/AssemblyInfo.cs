using Xunit;

// Tests that touch LiteDB each create their own in-memory database, but LiteDB maps entity types
// through a process-global BsonMapper.Global. Running test collections in parallel lets that global
// mapper be built concurrently, which intermittently throws inside GetCollection/Insert. The suite
// is tiny (~2s), so serializing collections trades no meaningful speed for a stable, green CI.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
