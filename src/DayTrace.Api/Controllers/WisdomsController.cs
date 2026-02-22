using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("wisdoms")]
public class WisdomsController : ControllerBase
{
    private readonly IWisdomRepository _wisdomRepo;

    public WisdomsController(IWisdomRepository wisdomRepo)
    {
        _wisdomRepo = wisdomRepo;
    }

    [HttpGet("random")]
    public async Task<IActionResult> GetRandom(CancellationToken ct)
    {
        var wisdom = await _wisdomRepo.GetRandomAsync(ct);
        if (wisdom == null)
            return NotFound(new { error = "not_found", message = "No wisdoms available" });

        return Ok(new
        {
            id = wisdom.Id,
            text = wisdom.Text,
            category = wisdom.Category,
            author = wisdom.Author
        });
    }
}
