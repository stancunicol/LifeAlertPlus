using System.Threading.Tasks;

namespace LifeAlertPlus.Application.IServices
{
    public interface IImportService
    {
        /// <summary>
        /// Încearcă să importe și să valideze datele dintr-un fișier JSON.
        /// </summary>
        /// <typeparam name="T">Tipul obiectelor așteptate în JSON.</typeparam>
        /// <param name="jsonContent">Conținutul fișierului JSON.</param>
        /// <returns>Rezultatul importului: succes, erori de format, erori de date.</returns>
        Task<LifeAlertPlus.Application.Services.ImportResult<T>> ImportAndValidateAsync<T>(string jsonContent) where T : class, new();
    }
}
