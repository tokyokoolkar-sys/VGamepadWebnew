using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace VGamepadWeb.Core
{
    public class ServerRunner
    {
        private WebApplication? _app;

        // الأحداث المركزية للـ WinForm لمنع الـ Null Reference
        public event Action<string, int>? OnControllerIdAssigned;
        public event Action<string>? OnPlayerDisconnected;

        public GamepadManager? GamepadManager => _app?.Services.GetRequiredService<GamepadManager>();
        public WebRTCSessionManager? WebRTCSessionManager => _app?.Services.GetRequiredService<WebRTCSessionManager>();

        // 🔥 تم تحديث الدالة لتقبل البورت وكلمة المرور وتشغيل الـ React على نفس السيرفر
        private int _port = 5000;

        private static string GetPreferredIp()
        {
            var candidates = new List<string>();
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip.Address))
                    {
                        candidates.Add(ip.Address.ToString());
                    }
                }
            }
            // Prefer hotspot subnet, then private LAN ranges.
            foreach (var c in candidates) if (c.StartsWith("192.168.137.")) return c;
            foreach (var c in candidates) if (c.StartsWith("192.168.") || c.StartsWith("10.") || c.StartsWith("172.")) return c;
            return candidates.Count > 0 ? candidates[0] : "127.0.0.1";
        }

        public async Task StartServerAsync(int port = 5000, string password = "")
        {
            _port = port;
            // تعيين كلمة المرور في الـ Hub فوراً قبل بدء تشغيل السيرفر لتأمين القنوات مباشرة
            GamepadHub.SetPassword(password);

            var builder = WebApplication.CreateBuilder();

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<GamepadManager>();
            builder.Services.AddSingleton<WebRTCSessionManager>();

            // Resolve the wwwroot folder robustly across host apps (WinForm/Console).
            string wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwrootPath))
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "wwwroot")))
                {
                    dir = dir.Parent;
                }
                if (dir != null) wwwrootPath = Path.Combine(dir.FullName, "wwwroot");
            }
            if (Directory.Exists(wwwrootPath))
            {
                builder.Environment.WebRootPath = wwwrootPath;
            }


            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    // Do not echo arbitrary origins with credentials; keep permissive
                    // for local-network clients but without credential-bearing responses.
                    policy.AllowAnyHeader()
                          .AllowAnyMethod()
                          .SetIsOriginAllowed(_ => true);
                });
            });

            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            _app = builder.Build();

            _app.UseCors();

            if (Directory.Exists(wwwrootPath))
            {
                var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwrootPath);
                _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
                _app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
                // Explicit root handler guarantees "/" serves the SPA entry point.
                _app.MapGet("/", () => Results.File(Path.Combine(wwwrootPath, "index.html"), "text/html"));
            }

            // خريطة الـ Hub الخاص بالـ SignalR
            _app.MapHub<GamepadHub>("/gamepadhub");

            // Simple health endpoint for monitoring/load balancers.
            _app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

            // Server info endpoint used by the UI to build a phone-friendly URL/QR.
            _app.MapGet("/api/info", () => Results.Ok(new { ip = GetPreferredIp(), port = _port, url = $"http://{GetPreferredIp()}:{_port}" }));

            // 🔥 3. حماية مسارات الـ React لضمان إعادة توجيه أي مسار عشوائي إلى صفحة الـ index.html لتجنب خطأ 404
            _app.MapFallbackToFile("index.html");

            var hubContext = _app.Services.GetRequiredService<IHubContext<GamepadHub>>();
            var rtcManager = _app.Services.GetRequiredService<WebRTCSessionManager>();
            var gamepadManager = _app.Services.GetRequiredService<GamepadManager>();

            // ربط الأحداث داخلياً فور بناء الـ App بنجاح
            gamepadManager.OnControllerIdAssigned += (connId, id) => OnControllerIdAssigned?.Invoke(connId, id);
            rtcManager.OnPlayerDisconnected += (connId) => OnPlayerDisconnected?.Invoke(connId);

            rtcManager.OnAnswerReady += async (connectionId, sdpAnswer) =>
            {
                await hubContext.Clients.Client(connectionId).SendAsync("ReceiveAnswer", sdpAnswer);
            };

            rtcManager.OnIceCandidateReady += async (connectionId, iceCandidate) =>
            {
                await hubContext.Clients.Client(connectionId).SendAsync("ReceiveIceCandidate", iceCandidate);
            };

            await _app.StartAsync();
        }

        public void UpdateServerPassword(string newPassword)
        {
            GamepadHub.SetPassword(newPassword);
        }

        public async Task StopServerAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }
        }
    }
}