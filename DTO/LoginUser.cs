namespace JaliscoProblemsAPI.DTOs.Auth;
using System.ComponentModel.DataAnnotations; 

public class LoginUserRequestDTO
{

    [Required(ErrorMessage = "Se necesita introducir un Email.")]
    [EmailAddress(ErrorMessage = "El Email introducido es incorrecto.")]
    [MaxLength(100, ErrorMessage = "El Email introducido es muy largo.")]
    public string Email {get; set; } = string.Empty;

    [Required(ErrorMessage = "No se introdujo una contraseña.")]
    [MinLength(8, ErrorMessage = "La contraseña introducida no es válida.")]
    [MaxLength(100, ErrorMessage = "La contraseña introducida es muy larga.")]
    public string Password {get; set; } = string.Empty;

}