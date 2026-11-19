namespace backend.Dtos;


using System.ComponentModel.DataAnnotations;

public sealed class UserLoginRequest
{
    public required string UsernameOrEmail { get; set; }
    public required string Password { get; set; }
}


public sealed class UserRegisterRequest
{
    [MinLength(3)]
    public required string UserName { get; set; }
    [EmailAddress]
    public required string Email { get; set; }
    [MinLength(6)]
    public required string Password { get; set; }

}

public sealed class UserLogingResponse
{
    public required string UserName { get; set; }
    public required string AccessToken { get; set; }    

}

public  sealed class UserRegisterResponse
{
    public required string UserName { get; set; }
}