using app_dev_assignment.Models;
using app_dev_assignment.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace app_dev_assignment.Controllers;

public class HomeController : Controller
{
    private const string VisitorCookieName = "mystik_visitor";
    private readonly IBlobService _blobService;
    private readonly IVisionService _visionService;
    private readonly IHistoryService _historyService;

    public HomeController(IBlobService blobService, IVisionService visionService, IHistoryService historyService)
    {
        _blobService = blobService;
        _visionService = visionService;
        _historyService = historyService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new ImageUploadViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ImageUploadViewModel model, CancellationToken cancellationToken)
    {
        var visitorId = GetOrCreateVisitorId();

        if (model.ImageFile is null || model.ImageFile.Length == 0)
        {
            ModelState.AddModelError("ImageFile", "Please select an image to analyze.");
            return View(model);
        }

        var uploadedUrl = await _blobService.UploadFileAsync(model.ImageFile, cancellationToken);
        await using var memoryStream = new MemoryStream();
        await model.ImageFile.CopyToAsync(memoryStream, cancellationToken);

        var analysis = await _visionService.AnalyzeImageAsync(uploadedUrl, memoryStream.ToArray(), cancellationToken);

        model.ImageUrl = uploadedUrl;
        model.Description = analysis.Description;
        model.Tags = analysis.Tags;
        model.IsCached = analysis.IsCached;

        _historyService.Add(visitorId, new HistoryItem
        {
            VisitorId = visitorId,
            ImageUrl = uploadedUrl,
            Description = analysis.Description,
            Tags = analysis.Tags,
            IsCached = analysis.IsCached,
            CreatedAt = DateTimeOffset.UtcNow
        });

        return View(model);
    }

    [HttpGet]
    public IActionResult History()
    {
        return View(_historyService.GetAll(GetOrCreateVisitorId()));
    }

    private string GetOrCreateVisitorId()
    {
        if (Request.Cookies.TryGetValue(VisitorCookieName, out var existingId) && Guid.TryParse(existingId, out _))
        {
            return existingId;
        }

        var visitorId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(VisitorCookieName, visitorId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            MaxAge = TimeSpan.FromDays(365)
        });

        return visitorId;
    }
}