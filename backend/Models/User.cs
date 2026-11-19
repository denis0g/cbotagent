using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public class User
{
    public Guid Id { get; set; }
    [Required]
    public string UserName { get; set; } = string.Empty;
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string HashPassword { get; set; } = string.Empty;
    public DateTime CreatedAt {get; set;} = DateTime.UtcNow;
    
}
