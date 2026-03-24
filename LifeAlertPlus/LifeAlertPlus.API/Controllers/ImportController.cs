using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]

    public class ImportController : ControllerBase
    {
        private readonly IImportService _importService;
        private readonly LifeAlertPlus.Infrastructure.Context.LifeAlertPlusDbContext _dbContext;
        public ImportController(IImportService importService, LifeAlertPlus.Infrastructure.Context.LifeAlertPlusDbContext dbContext)
        {
            _importService = importService;
            _dbContext = dbContext;
        }

        [HttpPost("esp-data")]
        public async Task<IActionResult> ImportESPData([FromBody] string jsonContent)
        {
            var result = await _importService.ImportAndValidateAsync<ESPDataResponseDTO>(jsonContent);
            if (!result.Success)
                return BadRequest(new { Errors = result.Errors });
            return Ok(new { Message = "Import reușit.", Data = result.Data });
        }

    }
}
