using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BusinessPartnerPortal.Api.Common;

/// <summary>
/// Resolves the frontend address from the server's active LAN interface.
/// This keeps links in email aligned with the machine that is serving the API.
/// </summary>
public static class FrontendUrlResolver
{
    private const int DefaultFrontendPort = 8088;

    public static string Resolve(IConfiguration configuration)
    {
        var configuredUrl = configuration["FRONTEND_URL"];
        if (!string.IsNullOrWhiteSpace(configuredUrl) &&
            !string.Equals(configuredUrl, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return configuredUrl.TrimEnd('/');
        }

        var scheme = configuration["FRONTEND_SCHEME"] ?? "http";
        var port = int.TryParse(configuration["FRONTEND_PORT"], out var configuredPort)
            ? configuredPort
            : DefaultFrontendPort;

        var lanAddress = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(unicastAddress => unicastAddress.Address)
            .FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address) &&
                !address.ToString().StartsWith("169.254.", StringComparison.Ordinal));

        var host = lanAddress?.ToString() ?? "localhost";
        return $"{scheme}://{host}:{port}";
    }
}
