using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VGamepadWeb.WinForm
{
    public static class NetworkHelper
    {
        /// <summary>
        /// 1. جلب جميع عناوين الـ IPv4 الشغالة في الجهاز مرتبة تلقائياً (الـ Wi-Fi في الصدارة)
        /// </summary>
        public static List<string> GetAllLocalIPv4Addresses()
        {
            List<string> ipList = new List<string>();

            // جلب كروت الشبكة النشطة وترتيبها بحيث يكون الـ Wireless أولاً
            var sortedInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(item =>
                    item.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                    item.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains("802.11", StringComparison.OrdinalIgnoreCase)
                );

            foreach (NetworkInterface item in sortedInterfaces)
            {
                IPInterfaceProperties adapterProperties = item.GetIPProperties();

                foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
                {
                    // تصفية العناوين لجلب IPv4 الحقيقية وتجاهل الـ Localhost (127.0.0.1)
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                    {
                        string ipAddress = ip.Address.ToString();

                        if (!ipList.Contains(ipAddress))
                        {
                            ipList.Add(ipAddress);
                        }
                    }
                }
            }

            return ipList;
        }

        /// <summary>
        /// 2. جلب عنوان الـ IPv4 المرجح والأكثر ملاءمة فقط (الـ Wi-Fi أولاً، ثم بقية الكروت، أو 127.0.0.1 كاحتياطي)
        /// </summary>
        public static string GetHotspotIPv4Address()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                string desc = ni.Description ?? "";
                string name = ni.Name ?? "";
                bool isHotspot = desc.Contains("Wi-Fi Direct Virtual", StringComparison.OrdinalIgnoreCase)
                    || desc.Contains("Microsoft Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Local Area Connection*", StringComparison.OrdinalIgnoreCase)
                    || desc.Contains("Hotspot", StringComparison.OrdinalIgnoreCase);
                if (!isHotspot) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
            foreach (var ipStr in GetAllLocalIPv4Addresses())
            {
                if (ipStr.StartsWith("192.168.137.")) return ipStr;
            }
            return "";
        }

        public static string GetPreferredLocalIPv4Address()
        {
            string hotspot = GetHotspotIPv4Address();
            if (!string.IsNullOrEmpty(hotspot)) return hotspot;

            var allIPs = GetAllLocalIPv4Addresses();
            if (allIPs != null && allIPs.Count > 0)
            {
                return allIPs[0];
            }
            return "127.0.0.1";
        }
    }
}