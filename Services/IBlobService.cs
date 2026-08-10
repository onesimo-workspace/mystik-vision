namespace app_dev_assignment.Services;

public interface IBlobService
{
    Task<string> UploadFileAsync(IFormFile file, CancellationToken cancellationToken = default);
}
