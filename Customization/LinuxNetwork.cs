using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EndfieldChargePlus.Customization;

internal sealed partial class LinuxVariableProvider
{
    private (long Tick, double Rx, double Tx, string Names)? _networkSample;

    private void AddLinuxNetwork(IDictionary<string, object?> v)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
        var ipv4 = new List<string>(); var ipv6 = new List<string>(); var gateways = new List<string>(); var dns = new List<string>();
        var rx = new List<double?>(); var tx = new List<double?>(); var speeds = new List<double>();
        foreach (var nic in interfaces)
        {
            rx.Add(Number(PathAt($"sys/class/net/{nic.Name}/statistics/rx_bytes")));
            tx.Add(Number(PathAt($"sys/class/net/{nic.Name}/statistics/tx_bytes")));
            var speed = Number(PathAt($"sys/class/net/{nic.Name}/speed"));
            if (speed > 0) speeds.Add(speed.Value * 1_000_000);
            try
            {
                var props = nic.GetIPProperties();
                ipv4.AddRange(props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()));
                ipv6.AddRange(props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal).Select(a => a.Address.ToString()));
                gateways.AddRange(props.GatewayAddresses.Select(a => a.Address.ToString()));
                dns.AddRange(props.DnsAddresses.Select(a => a.ToString()));
            }
            catch (NetworkInformationException) { }
        }
        string names = string.Join(", ", interfaces.Select(n => n.Name).Order());
        v["network.active_interface_count"] = interfaces.Length;
        v["network.interface_names"] = names;
        v["network.interface_types"] = string.Join(", ", interfaces.Select(n => n.NetworkInterfaceType.ToString()).Distinct());
        v["network.ipv4_addresses"] = string.Join(", ", ipv4.Distinct());
        v["network.ipv6_addresses"] = string.Join(", ", ipv6.Distinct());
        v["network.default_gateways"] = string.Join(", ", gateways.Distinct());
        v["network.dns_servers"] = string.Join(", ", dns.Distinct());
        v["network.available"] = interfaces.Length > 0;
        foreach (var (key, field) in new[] { ("packets_received", "rx_packets"), ("packets_sent", "tx_packets"), ("receive_errors", "rx_errors"), ("send_errors", "tx_errors") })
        {
            var counters = interfaces.Select(n => Number(PathAt($"sys/class/net/{n.Name}/statistics/{field}"))).ToArray();
            if (counters.All(n => n.HasValue)) v["network." + key] = counters.Sum(n => n!.Value);
        }
        if (rx.All(n => n.HasValue) && tx.All(n => n.HasValue))
        {
            double received = rx.Sum(n => n!.Value), sent = tx.Sum(n => n!.Value);
            v["network.total_received_bytes"] = received;
            v["network.total_sent_bytes"] = sent;
            v["network.total_transferred_bytes"] = received + sent;
            long now = Stopwatch.GetTimestamp();
            double? download = null, upload = null;
            if (interfaces.Length == 0) { download = 0; upload = 0; } // actual disconnected state
            else if (_networkSample is { } old && old.Names == names && received >= old.Rx && sent >= old.Tx)
            {
                double elapsed = Stopwatch.GetElapsedTime(old.Tick, now).TotalSeconds;
                if (elapsed > 0) { download = (received - old.Rx) / elapsed; upload = (sent - old.Tx) / elapsed; }
            }
            _networkSample = (now, received, sent, names);
            if (download.HasValue && upload.HasValue)
            {
                v["network.download_bps"] = download.Value; v["network.upload_bps"] = upload.Value;
                v["network.total_bps"] = download.Value + upload.Value;
                v["network.download_mbps"] = download.Value * 8 / 1_000_000;
                v["network.upload_mbps"] = upload.Value * 8 / 1_000_000;
                v["network.total_mbps"] = (download.Value + upload.Value) * 8 / 1_000_000;
                if (speeds.Count == interfaces.Length && speeds.Count > 0)
                    v["network.utilization_percent"] = Math.Clamp((download.Value + upload.Value) * 8 / speeds.Sum() * 100, 0, 100);
            }
        }
        if (speeds.Count > 0)
        {
            v["network.link_speed_bps"] = speeds.Sum(); v["network.max_link_speed_bps"] = speeds.Max();
        }
        try
        {
            var ip = IPGlobalProperties.GetIPGlobalProperties();
            v["network.tcp_connections"] = ip.GetActiveTcpConnections().Length;
            v["network.udp_connections"] = ip.GetActiveUdpListeners().Length;
        }
        catch (NetworkInformationException) { }
    }
}
