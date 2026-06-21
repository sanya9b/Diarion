using LiteDB;

namespace Diarion.Services.Database;

public interface IDatabaseContext
{
    ILiteCollection<T> GetCollection<T>(string name);
    string DatabasePath { get; }
    void Close();
}
