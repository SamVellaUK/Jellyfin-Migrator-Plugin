using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Template.Import;

/// <summary>
/// API controller to handle import ZIP upload and analysis.
/// </summary>
[ApiController]
[Route("JellyfinMigrator/Import")]
public class ImportController : ControllerBase
{
    private readonly ImportService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportController"/> class.
    /// </summary>
    /// <param name="paths">Application paths provider.</param>
    public ImportController(IApplicationPaths paths)
    {
        _service = new ImportService(paths, NullLogger<ImportService>.Instance);
    }

    /// <summary>
    /// Uploads a migration ZIP and returns an analysis breakdown of users and libraries.
    /// </summary>
    /// <param name="file">The uploaded ZIP file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 with analysis JSON, or 400 on bad input.</returns>
    [HttpPost("Upload")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new
            {
                Ok = false,
                Message = "No file uploaded.",
            });
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        ms.Seek(0, SeekOrigin.Begin);

        var result = await _service.ProcessZipAsync(ms, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
