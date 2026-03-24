using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAlertPlus.Application.IServices;

namespace LifeAlertPlus.Application.Services
{
    public class ImportResult<T>
    {
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<T>? Data { get; set; }
    }

    public class ImportService : IImportService
    {
        public async Task<ImportResult<T>> ImportAndValidateAsync<T>(string jsonContent) where T : class, new()
        {
            var result = new ImportResult<T>();

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                result.Errors.Add("Fișierul este gol.");
                return result;
            }

            try
            {
                var data = JsonSerializer.Deserialize<List<T>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (data == null || data.Count == 0)
                {
                    result.Errors.Add("Fișierul nu conține date valide.");
                    return result;
                }

                // Validare suplimentară pentru fiecare obiect (exemplu generic)
                foreach (var item in data)
                {
                    var validationErrors = ValidateItem(item);
                    if (validationErrors.Count > 0)
                        result.Errors.AddRange(validationErrors);
                }

                if (result.Errors.Count == 0)
                {
                    result.Success = true;
                    result.Data = data;
                }
            }
            catch (JsonException)
            {
                result.Errors.Add("Fișierul nu este un JSON valid.");
            }

            return result;
        }

        // Exemplu de validare generică (poți extinde pentru tipuri specifice)
        private List<string> ValidateItem<T>(T item)
        {
            var errors = new List<string>();
            if (item is LifeAlertPlus.Shared.DTOs.Responses.ESP.ESPDataResponseDTO dto)
            {
                // Validare: Serial nu trebuie să fie gol
                if (string.IsNullOrWhiteSpace(dto.Serial))
                    errors.Add("Serial lipsă sau gol.");
                // Validare: Date trebuie să fie > 0
                if (dto.Date <= 0)
                    errors.Add("Date invalid sau lipsă.");
                // Validare: Mpu6050 și Gyro trebuie să existe și să aibă exact 3 elemente
                if (dto.Mpu6050 == null || dto.Mpu6050.Count != 3)
                    errors.Add("Mpu6050 trebuie să aibă exact 3 valori (int)." );
                if (dto.Gyro == null || dto.Gyro.Count != 3)
                    errors.Add("Gyro trebuie să aibă exact 3 valori (int)." );
                // Validare: Max30100 dacă există, trebuie să aibă 2 valori
                if (dto.Max30100 != null && dto.Max30100.Count != 2)
                    errors.Add("Max30100 trebuie să aibă exact 2 valori (int) dacă există.");
                // Validare: Temperature și Battery dacă există, trebuie să fie numere
                // (nu e nevoie, tipul e deja double?)
                // Validare: Neo6m poate fi null sau string
                // Validare: ErrorMessage poate fi null sau string
                // Validare: IsAvailable este bool (tipul e deja corect)
            }
            else
            {
                errors.Add($"Tipul obiectului nu este ESPDataResponseDTO, ci {item?.GetType().Name ?? "null"}.");
            }
            return errors;
        }
    }
}
