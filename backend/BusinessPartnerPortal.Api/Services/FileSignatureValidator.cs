using BusinessPartnerPortal.Api.Common;

namespace BusinessPartnerPortal.Api.Services;

public static class FileSignatureValidator
{
    public static async Task ValidateAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0) throw new ApiException(400, "Uploaded file is empty.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);

        var valid = ext switch
        {
            ".pdf" => read >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D,
            ".png" => read >= 8 && header[..8].SequenceEqual(new byte[] { 0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A }),
            ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            _ => false
        };
        if (!valid) throw new ApiException(400, $"{file.FileName}: file content does not match its extension.");
    }
}
