using System.Net;

namespace Abo.Core.Connectors;

/// <summary>
/// Sicherheits-Hilfsmethoden für HTTP GET Requests.
/// Stellt SSRF-Schutz und Header-Injection-Prävention bereit.
/// In eigene Klasse ausgelagert für einfache Unit-Testbarkeit.
/// </summary>
public static class HttpGetSecurityHelper
{
    /// <summary>
    /// RFC-1918 private Adressbereiche und Sonderbereiche (Link-Local, Shared Address Space).
    /// Format: (Netzwerk-Adresse, Subnetzmaske) als uint für performante Bitmasken-Prüfung.
    /// </summary>
    private static readonly (uint Network, uint Mask)[] PrivateIpRanges =
    {
        (ParseIp("10.0.0.0"),    ParseIp("255.0.0.0")),     // RFC-1918: 10.0.0.0/8
        (ParseIp("172.16.0.0"),  ParseIp("255.240.0.0")),   // RFC-1918: 172.16.0.0/12
        (ParseIp("192.168.0.0"), ParseIp("255.255.0.0")),   // RFC-1918: 192.168.0.0/16
        (ParseIp("169.254.0.0"), ParseIp("255.255.0.0")),   // Link-local (APIPA)
        (ParseIp("100.64.0.0"),  ParseIp("255.192.0.0")),   // Shared Address Space RFC-6598
    };

    /// <summary>
    /// Prüft ob die URL auf eine private, loopback oder intern-routing Adresse zeigt (SSRF-Schutz).
    /// </summary>
    /// <param name="uri">Die zu prüfende URI.</param>
    /// <returns>Fehlermeldung wenn blockiert, null wenn die URL sicher ist.</returns>
    public static async Task<string?> CheckSsrfAsync(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();

        // Loopback-Hostnamen direkt blocken (ohne DNS-Auflösung)
        if (host == "localhost" || host == "127.0.0.1" || host == "::1" || host == "[::1]")
        {
            return $"Error (SSRF Protection): Requests to loopback addresses are not allowed. Host: {uri.Host}";
        }

        // IP-Adressen direkt prüfen (ohne DNS-Auflösung)
        if (IPAddress.TryParse(uri.Host, out var directIp))
        {
            if (IPAddress.IsLoopback(directIp))
            {
                return $"Error (SSRF Protection): Requests to loopback addresses are not allowed. Host: {uri.Host}";
            }
            if (IsPrivateIpAddress(directIp))
            {
                return $"Error (SSRF Protection): Requests to private/internal IP addresses are not allowed (RFC-1918). Host: {uri.Host}";
            }
            return null; // Öffentliche IP: OK
        }

        // Hostname via DNS auflösen und alle resultierenden Adressen prüfen
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);
            foreach (var address in addresses)
            {
                if (IPAddress.IsLoopback(address))
                {
                    return $"Error (SSRF Protection): Hostname '{uri.Host}' resolves to a loopback address ({address}).";
                }
                if (IsPrivateIpAddress(address))
                {
                    return $"Error (SSRF Protection): Hostname '{uri.Host}' resolves to a private/internal IP address ({address}). RFC-1918 addresses are blocked.";
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error (DNS Resolution): Could not resolve hostname '{uri.Host}': {ex.Message}";
        }

        return null; // Alle Checks bestanden: URL ist sicher
    }

    /// <summary>
    /// Prüft ob eine IP-Adresse in einem privaten RFC-1918-Bereich, Link-Local oder
    /// Shared-Address-Space liegt.
    /// </summary>
    /// <param name="address">Die zu prüfende IP-Adresse.</param>
    /// <returns>true wenn die Adresse privat/intern ist und blockiert werden soll.</returns>
    public static bool IsPrivateIpAddress(IPAddress address)
    {
        // IPv4: Bitmasken-Prüfung gegen alle definierten Ranges
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var ipAsUint = ((uint)bytes[0] << 24)
                         | ((uint)bytes[1] << 16)
                         | ((uint)bytes[2] << 8)
                         | bytes[3];

            foreach (var (network, mask) in PrivateIpRanges)
            {
                if ((ipAsUint & mask) == network)
                    return true;
            }
        }

        // IPv6: Unique Local Addresses (fc00::/7) blocken
        // fc00::/7 bedeutet: erstes Byte & 0xFE == 0xFC
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Prüft ob ein HTTP-Header auf der Sperrliste steht (Header Injection Schutz).
    /// Verhindert Manipulation von System-Headern durch externe Eingaben.
    /// </summary>
    /// <param name="headerName">Der zu prüfende Header-Name (case-insensitiv).</param>
    /// <returns>true wenn der Header blockiert werden soll.</returns>
    public static bool IsRestrictedHeader(string headerName)
    {
        var restricted = new[]
        {
            "Host",
            "Content-Length",
            "Transfer-Encoding",
            "Connection",
            "Upgrade",
            "Proxy-Authorization",
            "Proxy-Connection"
        };
        return restricted.Any(r => r.Equals(headerName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Konvertiert einen IPv4-String in einen uint für performante Bitmasken-Bereichsprüfungen.
    /// </summary>
    private static uint ParseIp(string ip)
    {
        var parts = ip.Split('.');
        return ((uint)byte.Parse(parts[0]) << 24)
             | ((uint)byte.Parse(parts[1]) << 16)
             | ((uint)byte.Parse(parts[2]) << 8)
             | byte.Parse(parts[3]);
    }
}
