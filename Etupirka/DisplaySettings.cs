﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Etupirka
{
    class DisplaySettings
    {
        #region WINAPI

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [Flags()]
        private enum DisplayDeviceStateFlags : int
        {
            /// <summary>The device is part of the desktop.</summary>
            AttachedToDesktop = 0x1,
            MultiDriver = 0x2,
            /// <summary>The device is part of the desktop.</summary>
            PrimaryDevice = 0x4,
            /// <summary>Represents a pseudo device used to mirror application drawing for remoting or other purposes.</summary>
            MirroringDriver = 0x8,
            /// <summary>The device is VGA compatible.</summary>
            VGACompatible = 0x10,
            /// <summary>The device is removable; it cannot be the primary display.</summary>
            Removable = 0x20,
            /// <summary>The device has more display modes than its output devices support.</summary>
            ModesPruned = 0x8000000,
            Remote = 0x4000000,
            Disconnect = 0x2000000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            [MarshalAs(UnmanagedType.U4)]
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            [MarshalAs(UnmanagedType.U4)]
            public DisplayDeviceStateFlags StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        #endregion

        private static DisplayInfo lastDisplayInfo = null;
        private static GameInfo currentGame = null;

        public static DisplayInfo GetDisplayDevices()
        {
            DisplayInfo displayDevices = new DisplayInfo();
            Match match;

            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);
            for (uint id = 0; EnumDisplayDevices(null, id, ref d, 0); id++)
            {
                if (d.StateFlags.HasFlag(DisplayDeviceStateFlags.AttachedToDesktop))
                {
                    d.cb = Marshal.SizeOf(d);
                    EnumDisplayDevices(d.DeviceName, 0, ref d, 0);
                    if ((match = new Regex(@"^MONITOR\\(.*?)\\").Match(d.DeviceID)).Success)
                    {
                        var deviceID = match.Groups[1].Value;
                        if (deviceID == "Default_Monitor") deviceID += getComputerID();
                        displayDevices.devices.Add(new DisplayDeviceInfo() { DeviceString = d.DeviceString, DeviceID = deviceID, Scaling = getDeviceScaling(deviceID) });
                    }
                }
                d.cb = Marshal.SizeOf(d);
            }
            return displayDevices;
        }

        public static string getComputerID()
        {
            string uuid = string.Empty;

            ManagementClass mc = new ManagementClass("Win32_ComputerSystemProduct");
            ManagementObjectCollection moc = mc.GetInstances();

            foreach (ManagementObject mo in moc)
            {
                uuid = mo.Properties["UUID"].Value.ToString();
                break;
            }

            return uuid;
        }

        public static void AdjustDisplay(DisplayInfo displayInfo, GameInfo game = null)
        {
            if (currentGame != null && game != null) return;

            bool displayChanged = false;
            DisplayInfo current = GetDisplayDevices();
            lastDisplayInfo = new DisplayInfo();

            foreach (var device in displayInfo.devices)
            {
                if (device.Enabled && current.devices.Any(_device => device.DeviceID == _device.DeviceID) && device.Scaling != getDeviceScaling(device.DeviceID))
                {
                    lastDisplayInfo.devices.Add(new DisplayDeviceInfo() { DeviceID = device.DeviceID, Scaling = getDeviceScaling(device.DeviceID), Enabled = true });
                    if (setDeviceScaling(device.DeviceID, device.Scaling))
                    {
                        displayChanged = true;
                    }
                }
            }

            if (displayChanged)
            {
                currentGame = game;
                restartDisplayDrivers();
            }
            else
            {
                lastDisplayInfo = null;
            }
        }

        public static void RestoreDisplay(GameInfo game = null)
        {
            if (lastDisplayInfo != null && game == currentGame)
            {
                AdjustDisplay(lastDisplayInfo);
                lastDisplayInfo = null;
                currentGame = null;
            }
        }

        private static int getDeviceScaling(string deviceID)
        {
            if (deviceID.StartsWith("Default_Monitor")) deviceID = "NOEDID";

            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\PerMonitorSettings");
            if (key == null) return 0;
            foreach (var keyName in key.GetSubKeyNames())
            {
                if (keyName.StartsWith(deviceID))
                {
                    return (int)key.OpenSubKey(keyName).GetValue("DpiValue", 0);
                }
            }
            return 0;
        }

        private static bool setDeviceScaling(string deviceID, int scaling)
        {
            if (deviceID.StartsWith("Default_Monitor")) deviceID = "NOEDID";

            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\PerMonitorSettings", true);
            if (key == null) return false;
            foreach (var keyName in key.GetSubKeyNames())
            {
                if (keyName.StartsWith(deviceID))
                {
                    key.OpenSubKey(keyName, true).SetValue("DpiValue", scaling, RegistryValueKind.DWord);
                    return true;
                }
            }
            return false;
        }

        private static void restartDisplayDrivers()
        {
            string path = Path.Combine(Path.GetTempPath(), "devcon.exe");
            try
            {
                var devconExe = Environment.Is64BitOperatingSystem ? Properties.Resources.devcon64 : Properties.Resources.devcon;
                File.WriteAllBytes(path, devconExe);
                Process.Start(new ProcessStartInfo(path, "restart =display")
                {
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath),
                    WindowStyle = ProcessWindowStyle.Hidden
                }).WaitForExit();
            }
            catch { }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
