using SIPSorcery.Net;
using System.Collections.Concurrent;
using System.Text;

namespace VGamepadWeb.Core
{
    public class WebRTCSessionManager
    {
        private readonly GamepadManager _gamepadManager;
        private readonly ConcurrentDictionary<string, RTCPeerConnection> _peerConnections = new();
        private readonly ConcurrentDictionary<string, RTCDataChannel> _dataChannels = new();

        public event Action<string, string> OnIceCandidateReady;
        public event Action<string, string> OnAnswerReady;
        public event Action<string> OnPlayerDisconnected; // حدث الـ WinForm

        public WebRTCSessionManager(GamepadManager gamepadManager)
        {
            _gamepadManager = gamepadManager;
            _gamepadManager.OnVibrationReceived += HandleVibrationFromGame;
            _gamepadManager.OnControllerIdAssigned += (connectionId, controllerId) =>
            {
                if (_dataChannels.TryGetValue(connectionId, out var dc) && dc.readyState == RTCDataChannelState.open)
                {
                    dc.send($"ID:{controllerId}");
                }
            };
        }

        private void HandleVibrationFromGame(string connectionId, byte largeMotor, byte smallMotor)
        {
            byte maxVibration = Math.Max(largeMotor, smallMotor);
            if (_dataChannels.TryGetValue(connectionId, out var dc) && dc.readyState == RTCDataChannelState.open)
            {
                dc.send($"V:{maxVibration}");
            }
        }

        public async Task StartConnectionAsync(string connectionId, string sdpOffer, TypeController type, bool enableVib, int sensitivity)
        {
            var pc = new RTCPeerConnection();
            _peerConnections.TryAdd(connectionId, pc);

            pc.ondatachannel += (dc) =>
            {
                Console.WriteLine($"[WebRTC] Data Channel Opened for: {connectionId}");
                _dataChannels.TryAdd(connectionId, dc);

                int controllerId = _gamepadManager.GetControllerId(connectionId);
                if (controllerId != -1 && dc.readyState == RTCDataChannelState.open)
                {
                    dc.send($"ID:{controllerId}");
                }

                dc.onmessage += (channel, protocol, data) =>
                {
                    ProcessFastMessage(connectionId, data, dc);
                };
            };

            pc.onicecandidate += (candidate) => { OnIceCandidateReady?.Invoke(connectionId, candidate.toJSON()); };

            pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdpOffer });
            var answer = pc.createAnswer(null);
            await pc.setLocalDescription(answer);

            OnAnswerReady?.Invoke(connectionId, answer.sdp);
            _gamepadManager.ConnectNewGamepad(connectionId, type, enableVib, sensitivity);
        }

        public void AddIceCandidate(string connectionId, string iceCandidateJson)
        {
            if (_peerConnections.TryGetValue(connectionId, out var pc) && RTCIceCandidateInit.TryParse(iceCandidateJson, out var candidateInit))
            {
                pc.addIceCandidate(candidateInit);
            }
        }

        private void ProcessFastMessage(string connectionId, byte[] data, RTCDataChannel dc)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                // Binary protocol: [0x02][stick: 0=Left,1=Right][x int16 LE][y int16 LE]
                if (data.Length >= 6 && data[0] == 0x02)
                {
                    string stickName = data[1] == 0 ? "Left" : "Right";
                    short x = BitConverter.ToInt16(data, 2);
                    short y = BitConverter.ToInt16(data, 4);
                    _gamepadManager.OnJoystickMove(connectionId, stickName, x, y);
                    return;
                }

                // Text protocol fallback (buttons, ping, legacy clients).
                string message = Encoding.UTF8.GetString(data);
                if (string.IsNullOrWhiteSpace(message)) return;

                var parts = message.Split(':');
                if (parts.Length == 0) return;

                if (parts[0] == "Ping")
                {
                    if (parts.Length >= 2) dc.send($"Pong:{parts[1]}");
                    return;
                }

                if (parts[0] == "B" && parts.Length >= 3) // Button press/release
                {
                    var buttonName = parts[1];
                    if (!string.IsNullOrWhiteSpace(buttonName))
                    {
                        _gamepadManager.OnButtonReceive(connectionId, buttonName, parts[2] == "1");
                    }
                }
                else if (parts[0] == "J" && parts.Length >= 4) // Legacy text joystick
                {
                    var stickName = parts[1];
                    if (!string.IsNullOrWhiteSpace(stickName)
                        && short.TryParse(parts[2], out var x)
                        && short.TryParse(parts[3], out var y))
                    {
                        _gamepadManager.OnJoystickMove(connectionId, stickName, x, y);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebRTC] Failed to process message: {ex.Message}");
            }
        }

        public void EndConnection(string connectionId)
        {
            if (_peerConnections.TryRemove(connectionId, out var pc)) pc.Close("User Disconnected");
            _dataChannels.TryRemove(connectionId, out _);
            _gamepadManager.DisconnectGamepad(connectionId);
            OnPlayerDisconnected?.Invoke(connectionId); // إعلام الـ WinForm بحذفه فوراً
        }
    }
}