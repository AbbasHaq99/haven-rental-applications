using Haven.Web.Domain;
using Haven.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace Haven.Web.Controllers;

public class AccountController(UserManager<AppUser> users, SignInManager<AppUser> signIn) : Controller
{
    [AllowAnonymous, HttpGet] public IActionResult Register() => View(new RegisterInput());
    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (input.Role is not (Roles.Applicant or Roles.Manager)) ModelState.AddModelError(nameof(input.Role), "Choose a valid role.");
        if (!ModelState.IsValid) return View(input);
        var user = new AppUser { UserName = input.Email.Trim(), Email = input.Email.Trim(), FullName = input.FullName.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded)
        {
            var roleResult = await users.AddToRoleAsync(user, input.Role);
            if (!roleResult.Succeeded) { await users.DeleteAsync(user); foreach (var e in roleResult.Errors) ModelState.AddModelError("", e.Description); return View(input); }
            await signIn.SignInAsync(user, false); return RedirectToAction("Index", "Properties");
        }
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View(input);
    }
    [AllowAnonymous, HttpGet] public IActionResult Login(string? returnUrl) => View(new LoginInput { ReturnUrl = returnUrl });
    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var result = await signIn.PasswordSignInAsync(input.Email.Trim(), input.Password, false, true);
        if (result.Succeeded) return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction("Index", "Properties");
        ModelState.AddModelError("", result.IsLockedOut ? "Too many login attempts. Try again in 15 minutes." : "Invalid email or password.");
        return View(input);
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction(nameof(Login)); }
    [AllowAnonymous, HttpGet] public IActionResult Denied() { Response.StatusCode = 403; return View(); }
}
