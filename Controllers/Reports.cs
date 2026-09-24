using Microsoft.AspNetCore.Mvc;
using JaliscoProblemsAPI.DTOs.Reports;
using Microsoft.AspNetCore.Authorization; // we use this to add the Authorize attribute.
using JaliscoProblemsAPI.Data.ReportRepository;

// models
using JaliscoProblemsAPI.Models.Report;

namespace JaliscoProblemsAPI.Controllers
{
    [ApiController]
    [Route("[controller]/")]

    public class ReportsController : ControllerBase
    {

        private ReportRepository _ReportRepository;

        public ReportsController(ReportRepository report_repository)
        {
            _ReportRepository = report_repository;
        }

        [Authorize] // we use the Authorize attribute for functions that need to use authorization to let the request do something. in this case we are creating a report, and-
        // only users who are logged in can be authorized to create them. so we put the authorize here.
        // we already have set the authorization and authentication in the program.cs to properly manage this Authorize.
        // the client now has to sent an "Authorization" header so the authorization can work properly. otherwise we send an error here.
        // in the authorization header, if we are working from the frontend we should send it as a header as the following:
        // Authorization: Bearer TOKEN
        // so basically we have to keep the token in the client to make requests to the backend that require authentication/authorization.
        [HttpPost("create")]
        public async Task<IActionResult> CreateReport([FromBody] CreateReportRequestDTO request)
        {

            // we basically get the user_id associated with the claim. as we remember, in the token service we send a claim with some of the user data which is useful in this case.
            // we obtain this value through the authentication. depending on the result of that we obtain this, so if a user is not authenticated then we dont obtain anything.
            string? user_id = User.FindFirst("user_id")?.Value;
            // the User.FindFirst does not represent a user from our models or classes, but represents the user associated with the execution of the action. it's a deserved name-
            // for System.Security.Claims.ClaimsPrincipal. we use FindFirst to get the claim associated with the value. in this case we want "user_id" and turn it into value.
            // remember that all of the claims values are strings, so we use string for this user_id variable

            // if the user id is null, then this means the authentication failed, so we return an Unauthorized message.

            // in this case with this if we can check both if the number is null and if it can be converted to a number.
            // we use tryparse to try parsing the user_id to user_id_integer. in case it cants, then we return the unauthorized response.
            // at the same time, we declare the user_id_integer inside here, and use that value later
            // we use out to put the result in the user_id_integer variable if the parsing was successful. otherwise we enter the if with the unauthorized response.
            if (!int.TryParse(user_id, out int user_id_integer)) return Unauthorized(new { message = "El usuario no esta autenticado." });

            // if the request does not contain the required attributes in the body we show a message.
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // if everything went well and no problems, we can now proceed creating the report.

            // we create a new report object with the required data to be sent to the CreateReport function in the ReportRepository
            Report report = new Report
            {
                user_id = user_id_integer,
                latitude = request.latitude,
                longitude = request.longitude,
                description = request.description,
                type = request.type,
            };

            // we await for the function to create the report
            await _ReportRepository.CreateReport(report);

            // and finally we send the Ok response if everything went well.
            return Ok(new { message = "El reporte se creo con exito." });


        }

        // this will respond to just /reports, as we don't specify a name here.
        [HttpGet]
        public async Task<IActionResult> GetAllReports()
        {

            var reports = await _ReportRepository.GetAllReports();
            
            return Ok(new
            {
                message = "Los reportes se obtuvieron con exito.",
                reports = reports,
            });
        }

        [HttpGet("search")]
        public async Task<IActionResult> GetFilteredReports([FromQuery] string query)
        {

            var reports = await _ReportRepository.GetFilteredReports(query);
            
            return Ok(new
            {
                message = "Los reportes filtrados se obtuvieron con exito.",
                reports = reports,
            });
        }

    }



}