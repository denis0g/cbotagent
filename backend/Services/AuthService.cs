using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using backend.Dtos;
using backend.Models;
using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services;

public class AuthService(AppDatabaseContext db, IConfiguration configuration)
{

    private readonly PasswordHasher<User> passwordHasher = new();


    public async Task<ServiceResult<UserLogingResponse>> Login(UserLoginRequest request)
    {
        var identifier = request.UsernameOrEmail.Trim();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.UserName == identifier ||
            u.Email == identifier);

        if (user == null)
        {
            return ServiceResult<UserLogingResponse>.Failure("Invalid username/email or password");
        }

        PasswordVerificationResult result = passwordHasher.VerifyHashedPassword(user, user.HashPassword, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            return ServiceResult<UserLogingResponse>.Failure("Invalid username/email or password");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.HashPassword = passwordHasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync();
        }

        return ServiceResult<UserLogingResponse>.Success(new()
        {
            UserName = user.UserName,
            AccessToken = GetToken(user)
        });
    }

    public async Task<ServiceResult<UserRegisterResponse>> Register(UserRegisterRequest request)
    {
        var userExist = await db.Users.AnyAsync(u => u.UserName == request.UserName || u.Email == request.Email);

        if (userExist)
            return ServiceResult<UserRegisterResponse>.Failure("User with UserName/Email exist");

        User newUser = new()
        {
            UserName = request.UserName,
            Email = request.Email,
        };

        newUser.HashPassword = passwordHasher.HashPassword(newUser, request.Password);

        await db.Users.AddAsync(newUser);
        await db.SaveChangesAsync();

        return ServiceResult<UserRegisterResponse>.Success(new()
        {
            UserName = newUser.UserName
        });
    }

    internal string GetToken(User user)
    {
        var claims = new List<Claim>{
            new Claim(ClaimTypes.Name, user.UserName.ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(configuration.GetValue<string>("Jwt:SecurityKey")!)
        );

        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: configuration.GetValue<string>("Jwt:Issuer"),
            audience: configuration.GetValue<string>("Jwt:Audience"),
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(configuration.GetValue<int>("Jwt:ExpiresMinutes")),
            signingCredentials: signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
    }
}
