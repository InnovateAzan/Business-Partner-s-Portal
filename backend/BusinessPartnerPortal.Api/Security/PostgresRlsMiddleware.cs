using System.Security.Claims;
using BusinessPartnerPortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Security;

public sealed class PostgresRlsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var userType = context.User.FindFirstValue("user_type") ?? "";
        var userIdRaw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? vendorId = null;
        if (Guid.TryParse(userIdRaw, out var userId) && userType.Equals("VENDOR", StringComparison.OrdinalIgnoreCase))
        {
            vendorId = await db.VendorUsers.AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive)
                .Select(x => (Guid?)x.VendorId)
                .FirstOrDefaultAsync(context.RequestAborted);
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(context.RequestAborted);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT set_config('app.vendor_id', @vendor, false), set_config('app.is_internal', @internal, false)";
                var p1 = command.CreateParameter(); p1.ParameterName = "vendor"; p1.Value = vendorId?.ToString() ?? ""; command.Parameters.Add(p1);
                var p2 = command.CreateParameter(); p2.ParameterName = "internal"; p2.Value = userType.Equals("VENDOR", StringComparison.OrdinalIgnoreCase) ? "false" : "true"; command.Parameters.Add(p2);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await next(context);
        }
        finally
        {
            await using var reset = connection.CreateCommand();
            reset.CommandText = "SELECT set_config('app.vendor_id', '', false), set_config('app.is_internal', 'false', false)";
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
            if (openedHere) await connection.CloseAsync();
        }
    }
}
