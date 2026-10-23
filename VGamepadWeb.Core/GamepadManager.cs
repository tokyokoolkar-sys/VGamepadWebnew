using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System.Collections.Concurrent;

namespace VGamepadWeb.Core
{
    public enum TypeController
    {
        Xbox360,
        DualShock4
    }

    public class GamepadSettings
    {
        public GamepadSettings(string connectionId, string clientId, bool enableVibration, int joystickSensitivity, TypeController controllerType, IVirtualGamepad gamepad, int controllerIndex)
        {
            ConnectionId = connectionId;
            ClientId = clientId;
            EnableVibration = enableVibration;
            JoystickSensitivity = joystickSensitivity;
            ControllerType = controllerType;
            Gamepad = gamepad;
            ControllerIndex = controllerIndex;
        }

        public string ConnectionId { get; set; }
        public string ClientId { get; set; } = "";
        public bool EnableVibration { get; set; }
        public int JoystickSensitivity { get; set; }
        public TypeController ControllerType { get; set; }
        public IVirtualGamepad Gamepad { get; set; }
        public int ControllerIndex { get; set; }
        // Bitmask for DS4 D-pad directions: 1=Up,2=Down,4=Left,8=Right
        public int DpadMask { get; set; }
    }

    public class GamepadManager : IDisposable
    {
        private ViGEmClient client;
        private readonly ConcurrentDictionary<string, GamepadSettings> _controllers = new();

        // نظام ذكي لإدارة الأرقام المتاحة تبدأ برمجياً من 0 إلى 3 تطابقاً مع نظام الـ XInput والـ UI للمحاكيات
        private readonly bool[] _availableIndexes = new bool[] { true, true, true, true };
        private readonly object _indexLock = new object();

        public event Action<string, byte, byte> OnVibrationReceived;

        // حدث إعلام السيرفر برقم اليد الذي حصل عليه اللاعب فوراً
        public event Action<string, int> OnControllerIdAssigned;

        // قاموس إكس بوكس
        private readonly Dictionary<string, Xbox360Button> _buttonMapXbox = new(StringComparer.OrdinalIgnoreCase)
        {
            {"A", Xbox360Button.A}, {"B", Xbox360Button.B}, {"X", Xbox360Button.X}, {"Y", Xbox360Button.Y},
            {"Up", Xbox360Button.Up}, {"Down", Xbox360Button.Down}, {"Left", Xbox360Button.Left}, {"Right", Xbox360Button.Right},
            {"Start", Xbox360Button.Start}, {"Back", Xbox360Button.Back}, {"LB", Xbox360Button.LeftShoulder}, {"RB", Xbox360Button.RightShoulder},
            {"LS", Xbox360Button.LeftThumb}, {"RS", Xbox360Button.RightThumb},
            {"Xbox", Xbox360Button.Guide}
        };

        // قاموس بلايستيشن الموحد
        private readonly Dictionary<string, DualShock4Button> _buttonMapDualShock4 = new(StringComparer.OrdinalIgnoreCase)
        {
            {"A", DualShock4Button.Cross}, {"B", DualShock4Button.Circle}, {"X", DualShock4Button.Square}, {"Y", DualShock4Button.Triangle},
            {"Start", DualShock4Button.Options}, {"Back", DualShock4Button.Share}, {"LB", DualShock4Button.ShoulderLeft}, {"RB", DualShock4Button.ShoulderRight},
            {"LS", DualShock4Button.ThumbLeft}, {"RS", DualShock4Button.ThumbRight},
            {"Xbox", DualShock4SpecialButton.Ps},
            {"Touchpad", DualShock4SpecialButton.Touchpad}
        };

        public GamepadManager()
        {
            client = new ViGEmClient();
        }

        public void ConnectNewGamepad(string connectionId, string clientId = "", TypeController type = TypeController.Xbox360, bool enableVib = true, int sensitivity = 100)
        {
            if (_controllers.ContainsKey(connectionId)) return;

            // Reuse an existing virtual gamepad for a reconnecting client (remembered device).
            if (!string.IsNullOrEmpty(clientId))
            {
                var existing = _controllers.FirstOrDefault(kv => kv.Value.ClientId == clientId && kv.Key != connectionId);
                if (existing.Key != null && _controllers.TryRemove(existing.Key, out var existingSettings))
                {
                    existingSettings.ConnectionId = connectionId;
                    existingSettings.ClientId = clientId;
                    existingSettings.EnableVibration = enableVib;
                    existingSettings.JoystickSensitivity = sensitivity;
                    existingSettings.ControllerType = type;
                    if (_controllers.TryAdd(connectionId, existingSettings))
                    {
                        OnControllerIdAssigned?.Invoke(connectionId, existingSettings.ControllerIndex);
                        Console.WriteLine($"[Manager] Reused Controller ID: {existingSettings.ControllerIndex} for reconnecting client: {clientId}");
                        return;
                    }
                }
            }

            int assignedIndex = -1;
            lock (_indexLock)
            {
                for (int i = 0; i < _availableIndexes.Length; i++)
                {
                    if (_availableIndexes[i])
                    {
                        _availableIndexes[i] = false;
                        assignedIndex = i;
                        break;
                    }
                }
            }

            if (assignedIndex == -1)
            {
                Console.WriteLine($"[Manager] All controller slots in use; rejected player: {connectionId}");
                return;
            }

            IVirtualGamepad gamepad;
            GamepadSettings settings = null!;

            if (type == TypeController.DualShock4)
            {
                var ds4 = client.CreateDualShock4Controller();
                ds4.FeedbackReceived += (sender, args) =>
                {
                    if (enableVib && settings != null) OnVibrationReceived?.Invoke(settings.ConnectionId, args.LargeMotor, args.SmallMotor);
                };
                ds4.Connect();
                gamepad = ds4;
            }
            else
            {
                var x360 = client.CreateXbox360Controller();
                x360.FeedbackReceived += (sender, args) =>
                {
                    if (enableVib && settings != null) OnVibrationReceived?.Invoke(settings.ConnectionId, args.LargeMotor, args.SmallMotor);
                };
                x360.Connect();
                gamepad = x360;
            }

            settings = new GamepadSettings(connectionId, clientId, enableVib, sensitivity, type, gamepad, assignedIndex);
            _controllers.TryAdd(connectionId, settings);

            OnControllerIdAssigned?.Invoke(connectionId, assignedIndex);
            Console.WriteLine($"[Manager] Assigned Controller ID: {assignedIndex} to Player: {connectionId} (client: {clientId})");
        }

        public void DisconnectGamepad(string connectionId)
        {
            if (_controllers.TryRemove(connectionId, out var settings))
            {
                settings.Gamepad.Disconnect();

                lock (_indexLock)
                {
                    if (settings.ControllerIndex >= 0 && settings.ControllerIndex < 4)
                    {
                        _availableIndexes[settings.ControllerIndex] = true;
                        Console.WriteLine($"[Manager] Controller ID: ({settings.ControllerIndex}) is now FREE.");
                    }
                }
            }
        }

        public void OnButtonReceive(string connectionId, string buttonName, bool isPressed)
        {
            if (_controllers.TryGetValue(connectionId, out var settings))
            {
                if (settings.ControllerType == TypeController.Xbox360)
                {
                    var x360 = (IXbox360Controller)settings.Gamepad;

                    if (buttonName == "LT") x360.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(isPressed ? 255 : 0));
                    else if (buttonName == "RT") x360.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(isPressed ? 255 : 0));
                    else if (_buttonMapXbox.TryGetValue(buttonName, out var button))
                    {
                        x360.SetButtonState(button, isPressed);
                    }
                    x360.SubmitReport();
                }
                else if (settings.ControllerType == TypeController.DualShock4)
                {
                    var ds4 = (IDualShock4Controller)settings.Gamepad;

                    if (buttonName == "LT") ds4.SetSliderValue(DualShock4Slider.LeftTrigger, (byte)(isPressed ? 255 : 0));
                    else if (buttonName == "RT") ds4.SetSliderValue(DualShock4Slider.RightTrigger, (byte)(isPressed ? 255 : 0));
                    else if (buttonName == "Up" || buttonName == "Down" || buttonName == "Left" || buttonName == "Right")
                    {
                        int bit = buttonName == "Up" ? 1 : buttonName == "Down" ? 2 : buttonName == "Left" ? 4 : 8;
                        if (isPressed) settings.DpadMask |= bit; else settings.DpadMask &= ~bit;

                        DualShock4DPadDirection dir = DualShock4DPadDirection.None;
                        bool up = (settings.DpadMask & 1) != 0;
                        bool down = (settings.DpadMask & 2) != 0;
                        bool left = (settings.DpadMask & 4) != 0;
                        bool right = (settings.DpadMask & 8) != 0;
                        if (up && right) dir = DualShock4DPadDirection.Northeast;
                        else if (up && left) dir = DualShock4DPadDirection.Northwest;
                        else if (down && right) dir = DualShock4DPadDirection.Southeast;
                        else if (down && left) dir = DualShock4DPadDirection.Southwest;
                        else if (up) dir = DualShock4DPadDirection.North;
                        else if (down) dir = DualShock4DPadDirection.South;
                        else if (left) dir = DualShock4DPadDirection.West;
                        else if (right) dir = DualShock4DPadDirection.East;
                        ds4.SetDPadDirection(dir);
                    }
                    else if (_buttonMapDualShock4.TryGetValue(buttonName, out var button))
                    {
                        ds4.SetButtonState(button, isPressed);
                    }
                    ds4.SubmitReport();
                }
            }
        }

        public void OnJoystickMove(string connectionId, string stickName, short xValue, short yValue)
        {
            if (_controllers.TryGetValue(connectionId, out var settings))
            {
                // Apply configured joystick sensitivity (100 = no scaling).
                int sens = Math.Clamp(settings.JoystickSensitivity, 1, 1000);
                if (sens != 100)
                {
                    xValue = (short)Math.Clamp((int)xValue * sens / 100, short.MinValue, short.MaxValue);
                    yValue = (short)Math.Clamp((int)yValue * sens / 100, short.MinValue, short.MaxValue);
                }

                if (settings.ControllerType == TypeController.Xbox360)
                {
                    var x360 = (IXbox360Controller)settings.Gamepad;
                    if (stickName == "Left")
                    {
                        x360.SetAxisValue(Xbox360Axis.LeftThumbX, xValue);
                        x360.SetAxisValue(Xbox360Axis.LeftThumbY, yValue);
                    }
                    else if (stickName == "Right")
                    {
                        x360.SetAxisValue(Xbox360Axis.RightThumbX, xValue);
                        x360.SetAxisValue(Xbox360Axis.RightThumbY, yValue);
                    }
                    x360.SubmitReport();
                }
                else if (settings.ControllerType == TypeController.DualShock4)
                {
                    var ds4 = (IDualShock4Controller)settings.Gamepad;

                    byte ps4X = (byte)((xValue + 32768) / 256);
                    byte ps4Y = (byte)(255 - ((yValue + 32768) / 256));

                    if (stickName == "Left")
                    {
                        ds4.SetAxisValue(DualShock4Axis.LeftThumbX, ps4X);
                        ds4.SetAxisValue(DualShock4Axis.LeftThumbY, ps4Y);
                    }
                    else if (stickName == "Right")
                    {
                        ds4.SetAxisValue(DualShock4Axis.RightThumbX, ps4X);
                        ds4.SetAxisValue(DualShock4Axis.RightThumbY, ps4Y);
                    }
                    ds4.SubmitReport();
                }
            }
        }



        public int GetControllerId(string connectionId)
        {
            if (_controllers.TryGetValue(connectionId, out var settings))
            {
                return settings.ControllerIndex;
            }
            return -1;
        }

        // دالة مساعدة لـ WinForms لاستخراج نوع اليد كـ String لعرضه في الكرت
        public string GetControllerTypeString(string connectionId)
        {
            if (_controllers.TryGetValue(connectionId, out var settings))
            {
                return settings.ControllerType.ToString();
            }
            return "Unknown";
        }

        public void UpdateSettingLive(string connectionId, string settingName, string value)
        {
            if (!_controllers.TryGetValue(connectionId, out var settings)) return;

            if (settingName == "VibrationEnabled" && bool.TryParse(value, out var vib))
            {
                settings.EnableVibration = vib;
            }
            else if (settingName == "JoystickSensitivity" && int.TryParse(value, out var sens))
            {
                settings.JoystickSensitivity = Math.Clamp(sens, 1, 1000);
            }
        }

        public void Dispose()
        {
            foreach (var settings in _controllers.Values)
            {
                settings.Gamepad.Disconnect();
            }
            client?.Dispose();
        }
    }
}