using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class StatusController : ControllerBase
{
    private readonly ILogger<StatusController> _logger;

    public StatusController(ILogger<StatusController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        _logger.LogInformation("Requisição de status recebida às {Time}", DateTime.Now);
        return Ok(new { status = "Operacional" });
    }
}