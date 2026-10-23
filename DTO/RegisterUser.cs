namespace JaliscoProblemsAPI.DTOs.Auth;
using System.ComponentModel.DataAnnotations;
using JaliscoProblemsAPI.Infrastructure; // this is used for the [Required] and more stuff here in the class.

public class RegisterUserRequestDTO
{
    [Required(ErrorMessage = "Se necesita introducir un Email.")]
    [EmailAddress(ErrorMessage = "El Email introducido es incorrecto.")]
    [MaxLength(100, ErrorMessage = "El Email introducido es muy largo.")]
    public string Email {get; set; } = string.Empty;

    [Required(ErrorMessage = "No se introdujo una contraseña.")]
    [MinLength(8, ErrorMessage = "La contraseña introducida es muy corta.")]
    [MaxLength(100, ErrorMessage = "La contraseña introducida es muy larga.")]
    [Utf8PasswordLength]
    public string Password {get; set; } = string.Empty;

    [Required(ErrorMessage = "No se introdujo un nombre de usuario.")]
    [MinLength(5, ErrorMessage = "El usuario introducido es muy corto.")]
    [MaxLength(20, ErrorMessage = "El usuario introducido es muy largo.")]
    public string Username {get; set; } = string.Empty;

}