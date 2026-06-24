using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Shared.DTOs.Requests.ESP;

namespace LifeAlertPlus.API.Services
{
    // Interfața pentru SimulationManager — permite ESPController să citească date simulate
    // fără a cunoaște implementarea concretă (ConcurrentDictionary, loop-uri etc.)
    public interface ISimulationManager
    {
        // Returnează datele simulate curente pentru un dispozitiv după numărul de serie
        ESPDataResponseDTO? GetData(string serial);

        // Stochează un set nou de date simulate (puls, temperatură, SpO2, GPS etc.)
        void SetData(ESPDataResponseDTO payload);

        // Salvează ultimul heartbeat primit de la ESP (semnal de viață al dispozitivului)
        void SetHeartbeat(string serial, ESPHeartbeatDTO data);

        // Returnează ultimul heartbeat stocat + momentul recepției (pentru calculul vârstei semnalului)
        (DateTime ReceivedAt, ESPHeartbeatDTO Data)? GetHeartbeat(string serial);

        // Șterge datele simulate pentru un dispozitiv (la oprirea simulării sau la resetare)
        bool ClearData(string serial);
    }
}
