using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IAuxiliaryService
{
    Task<List<ReadingTrackerBook>> GetReadingTrackerBooksAsync();
    Task SaveReadingTrackerBookAsync(ReadingTrackerBook book);
    Task DeleteReadingTrackerBookAsync(int slotNumber);

    Task<List<HappyMoment>> GetHappyMomentsAsync();
    Task SaveHappyMomentAsync(HappyMoment moment);
    Task DeleteHappyMomentAsync(int slotNumber);

    Task<List<GoodDeed>> GetGoodDeedsAsync();
    Task SaveGoodDeedAsync(GoodDeed deed);
    Task DeleteGoodDeedAsync(int slotNumber);
}