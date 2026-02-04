namespace KanbanBackend.API.Services;

public interface IRateLimitingService
{
    void CheckRegisterLimit(string ipAddress);
    void CheckLoginLimit(string ipAddress);
    void CheckRefreshLimit(string ipAddress);
}
