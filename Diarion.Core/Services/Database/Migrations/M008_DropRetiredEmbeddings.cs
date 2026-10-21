using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Drops <c>ai_embeddings</c>, the vector index the on-device encoder kept over diary entries and notes.
/// The on-device AI was removed on 2026-10-21 (the owner: the indexing worked badly and added nothing),
/// and nothing reads the collection any more. Its rows were derived data — slices of the diary's own
/// text with a vector each, rebuildable from the entries — so dropping loses nothing the user wrote;
/// it frees space in the encrypted file, which carried several megabytes of vectors for a long diary.
/// Also cleans the collection out of an older backup when one is restored, because a restore reopens
/// the database through this runner. The name is spelled out: the constant went with the feature.
/// Idempotent: dropping a collection that is not there does nothing.
/// </summary>
public sealed class M008_DropRetiredEmbeddings : IMigration
{
    public const string RetiredCollection = "ai_embeddings";

    public int ToVersion => 8;

    public void Up(LiteDatabase db) => db.DropCollection(RetiredCollection);
}
