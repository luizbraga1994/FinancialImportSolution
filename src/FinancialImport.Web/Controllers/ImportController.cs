using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class ImportController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Please select a file to upload.");
            return View(nameof(Index));
        }

        // TODO: Implement file import processing
        await Task.CompletedTask;

        TempData["Success"] = "File uploaded successfully.";
        return RedirectToAction(nameof(Index));
    }
}
