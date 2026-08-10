using app_dev_assignment.Models;
using app_dev_assignment.Services;
using Microsoft.AspNetCore.Mvc;

namespace app_dev_assignment.Controllers;

public class HomeController : Controller
{
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
    public async Task<IActionResult> Index(ImageUploadViewModel model, CancellationToken cancellationToken)
    {
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

        _historyService.Add(new HistoryItem
        {
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
        var items = _historyService.GetAll();
        return View(items);
    }
}
