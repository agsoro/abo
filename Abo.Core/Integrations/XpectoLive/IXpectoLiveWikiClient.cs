using Abo.Integrations.XpectoLive.Models;

namespace Abo.Integrations.XpectoLive;

public interface IXpectoLiveWikiClient
{
    // Space Endpoints
    Task<Space[]> GetSpacesAsync();
    Task<Space> CreateSpaceAsync(SpaceNew spaceNew);
    Task<Space> GetSpaceAsync(string spaceId);
    Task<SpacePageInfo[]> GetSpaceInfoAsync(string spaceId);
    
    // Page Endpoints
    Task<Page> CreatePageAsync(string spaceId, PageNew pageNew);
    Task<Page> GetPageAsync(string spaceId, string pageId);
    Task<Page> UpdatePageDraftAsync(string spaceId, string pageId, ContentUpdate contentUpdate);
    Task<Page> PublishPageDraftAsync(string spaceId, string pageId);
    Task MovePageAsync(string spaceId, string pageId, MovePageRequest moveRequest);
    Task CopyPageAsync(string spaceId, string pageId, CopyPageRequest copyRequest);
    
    // Collaborative Endpoints
    Task JoinCollaborativeRoomAsync(string spaceId, string pageId, string clientId);
    Task LeaveCollaborativeRoomAsync(string spaceId, string pageId, string clientId);
    Task RdpAsync(string domain, string user, string computerName);
}
