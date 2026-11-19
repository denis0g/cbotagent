using Microsoft.AspNetCore.Mvc;
using backend.Services;
using backend.Dtos;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AuthService authService) : ControllerBase
{
    
    [HttpPost("register")]
    public async Task<ActionResult> Register(UserRegisterRequest request)
    {
        var result = await authService.Register(request);
        if (!result.IsSuccess)
            return BadRequest(result.ErrorMessage);
        else
            return Ok(result.Data);
    }

    [HttpPost("login")]
    public async Task<ActionResult> Login(UserLoginRequest request)
    {
        var result = await authService.Login(request);
        
        if (!result.IsSuccess)
            return BadRequest(result.ErrorMessage);
        else
            return Ok(result.Data);
    }
}
