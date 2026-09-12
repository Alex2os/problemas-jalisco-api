using Microsoft.AspNetCore.Mvc;
using JaliscoProblemsAPI.DTOs.Auth;
using JaliscoProblemsAPI.Data.UserRepository;
using JaliscoProblemsAPI.Services.PasswordService;
using JaliscoProblemsAPI.Models.User;
using JaliscoProblemsAPI.Services.TokenService;

namespace JaliscoProblemsAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class AuthController : ControllerBase
    {
        private UserRepository _UserRepository; // as the same as UserRepository, this variable is got from Program.cs, in this case UserRepository is scoped and not singleton like the database.
        private PasswordService _PasswordService; // variable got from program.cs. used for the password service.
        private TokenService _TokenService; // token service is another scoped class in the program.cs which we can receive here.
        // it's important to say that all the scoped / singleton classes declared in the program.cs can be accessed by any class in their constructor, declaring in the-
        // constructor that it will recieve said classes. it's not necessary to receive all the classes. if just some are needed then just those may be declared.

        // all of the variables we obtain from program.cs have to be obtained through here and asigned to our variables here in the controller in this case.
        public AuthController(UserRepository user_repository, PasswordService password_service, TokenService token_service)
        {
            _UserRepository = user_repository;
            _PasswordService = password_service;
            _TokenService = token_service;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserRequestDTO request)
        {

            // with modelstate we can check if the request contains the required fields. in this case, using .IsValid returns true or false. in case it's not valid, we return a bad request containing the modelstate, and it will show the error when returning that we specified in the DTO.
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // we check if the email and username exist, from the function EmailExists and UsernameExists from the user repository. Repositories are used to check database content.
            bool email_exists = await _UserRepository.EmailExists(request.Email);

            bool username_exists = await _UserRepository.UsernameExists(request.Username);

            // depending on the bool variables we send conflict messages or we create a new user.
            if (!email_exists && !username_exists)
            {

                // we hash the password that the user provided so we can store it in the database properly.
                string hashed_user_password = _PasswordService.HashPassword(request.Password);

                User user = new User
                {
                    
                    Username = request.Username,
                    Email = request.Email,
                    PasswordHash = hashed_user_password,

                };

                await _UserRepository.CreateUser(user);

                return Ok(new { message = "El usuario se ha creado con exito" });
            }
            // if the email exists, we return a conflict saying that the email already exists
            else if (email_exists) return Conflict(new { message = "El email ya existe.", });
            // for anything else (which is when the username already exists) we return a conflict saying that the username already exists.
            else return Conflict(new { message = "El nombre de usuario ya existe", });
        }

        // with the login function we check first if the user inserted a valid email checking in the database. then we obtain that user, and we check the hashed password-
        // of that user against the password introduced. in case everything is good, we return an Ok() response. otherwise we return an Unauthorized() response saying that the data introduced is not correct.
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginUserRequestDTO request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // we check if the email exists to proceed, otherwise we return a reponse saying that the data introduced is not correct
            bool email_exists = await _UserRepository.EmailExists(request.Email);
            if (!email_exists) return Unauthorized(new { message = "Los datos introducidos no son correctos." });

            // if the email exists, we get the user asociated to that email
            User user = await _UserRepository.GetUser(request.Email);

            // we check the hashed password from the user against the request.Password and save it on a bool variable to check
            bool password_hash_check = _PasswordService.CheckHashedPassword(request.Password, user.PasswordHash);

            if (email_exists && password_hash_check) // if the request passed all filters, then we allow the login
            {

                // we create a token for authentication/authorization. in this case we use the TokenService class we created to create a token and return it as a string here.

                string token = _TokenService.CreateToken(user);

                return Ok(new
                {
                    message = "Login completado con exito.",
                    token = token,
                    user = new
                    {
                        id = user.ID,
                        username = user.Username,
                        email = user.Email
                    }
                });
            }

            // otherwise we return an unauthorized response.
            else return Unauthorized(new { message = "Los datos introducidos no son correctos." });

        }

    }
}