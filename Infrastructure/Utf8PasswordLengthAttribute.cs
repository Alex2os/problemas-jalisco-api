using System.ComponentModel.DataAnnotations;
using System.Text;

namespace JaliscoProblemsAPI.Infrastructure;

// BCrypt only considers the first 72 bytes. Apply to registration, not legacy logins.
[AttributeUsage(AttributeTargets.Property)]
public sealed class Utf8PasswordLengthAttribute : ValidationAttribute
{
    public Utf8PasswordLengthAttribute() : base("La contraseña es demasiado larga. Usa menos caracteres.") { }
    public override bool IsValid(object? value) => value is not string password || Encoding.UTF8.GetByteCount(password) <= 72;
}
