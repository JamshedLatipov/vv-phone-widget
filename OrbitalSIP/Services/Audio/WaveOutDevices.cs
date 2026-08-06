using System;
using System.Runtime.InteropServices;

namespace OrbitalSIP.Services.Audio
{
    /// <summary>
    /// Enumerates waveOut playback devices.
    ///
    /// NAudio only exposes this through <c>WaveOut</c>, which lives in NAudio.WinForms and
    /// drags the entire Windows Desktop runtime pack (~50 MB of WPF and WinForms) into the
    /// publish. WASAPI's <c>MMDeviceEnumerator</c> is not a substitute: settings persist a
    /// waveOut device index that is later assigned to <c>WaveOutEvent.DeviceNumber</c>, and
    /// WASAPI enumerates in a different order, so the operator would silently end up on the
    /// wrong speakers. So we call the same winmm entry points NAudio calls internally.
    /// </summary>
    public static class WaveOutDevices
    {
        private const int MmSysErrNoError = 0;
        private const int MaxPnameLen = 32;

        /// <summary>Number of waveOut playback devices. Indices are 0..Count-1, matching <c>WaveOutEvent.DeviceNumber</c>.</summary>
        public static int Count
        {
            get
            {
                try { return waveOutGetNumDevs(); }
                catch (DllNotFoundException) { return 0; }
                catch (EntryPointNotFoundException) { return 0; }
            }
        }

        /// <summary>Friendly name of a playback device, or an empty string when the index is not a live device.</summary>
        public static string ProductName(int deviceNumber)
        {
            // Negative indices are WAVE_MAPPER, a routing directive rather than a device.
            // winmm happily answers for it, which would put a phantom entry in the list.
            if (deviceNumber < 0) return string.Empty;

            try
            {
                int result = waveOutGetDevCapsW(
                    (IntPtr)deviceNumber, out var caps, Marshal.SizeOf<WaveOutCaps>());

                if (result != MmSysErrNoError) return string.Empty;
                return (caps.ProductName ?? string.Empty).Trim();
            }
            catch (DllNotFoundException) { return string.Empty; }
            catch (EntryPointNotFoundException) { return string.Empty; }
        }

        /// <summary>WAVEOUTCAPSW. Field order and widths are fixed by winmm — do not reorder.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WaveOutCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;

            /// <summary>Inline 32-char buffer; ByValTStr stops at the terminator instead of handing back the padding.</summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPnameLen)]
            public string ProductName;

            public uint SupportedFormats;
            public ushort Channels;
            public ushort Reserved;
            public uint Support;
        }

        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern int waveOutGetNumDevs();

        [DllImport("winmm.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int waveOutGetDevCapsW(IntPtr deviceId, out WaveOutCaps caps, int capsSize);
    }
}
