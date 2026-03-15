using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class StatusCodeModel : PageModel
{
    public int Code { get; set; }

    public void OnGet(int code)
    {
        Code = code;
        Response.StatusCode = code;
    }
}
