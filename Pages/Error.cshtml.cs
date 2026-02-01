using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class ErrorModel : PageModel
{
    public void OnGet() { }
}
