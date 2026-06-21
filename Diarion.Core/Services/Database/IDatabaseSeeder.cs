using LiteDB;

namespace Diarion.Services.Database;

public interface IDatabaseSeeder
{
    void Seed(LiteDatabase database);
}