using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Shared.DTOs.Requests.ESP;

namespace LifeAlertPlus.API.Services
{
    public interface ISimulationManager
    {
        ESPDataResponseDTO? GetData(string serial);
        void SetData(ESPDataResponseDTO payload);
        void SetHeartbeat(string serial, ESPHeartbeatDTO data);
        (DateTime ReceivedAt, ESPHeartbeatDTO Data)? GetHeartbeat(string serial);
        bool ClearData(string serial);
    }
}
