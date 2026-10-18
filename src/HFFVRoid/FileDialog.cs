using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HFFVRoid
{
    /// <summary>
    /// Native Windows "Open file" dialog (comdlg32). Runs on a background STA thread so the game keeps
    /// rendering; poll <see cref="TryGetResult"/> from Update.
    /// </summary>
    internal sealed class FileDialog
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_EXPLORER = 0x00080000;
        private const int OFN_HIDEREADONLY = 0x00000004;

        private volatile bool done;
        private string result;
        private string error;

        public bool IsOpen { get; private set; }

        public void Open(string title, string initialDir)
        {
            if (IsOpen) return;
            IsOpen = true;
            done = false;
            result = null;
            error = null;
            IntPtr owner = IntPtr.Zero;
            try { owner = GetActiveWindow(); } catch { }
            var thread = new Thread(() =>
            {
                IntPtr buffer = IntPtr.Zero;
                try
                {
                    const int max = 4096;
                    buffer = Marshal.AllocHGlobal(max * 2);
                    Marshal.WriteInt16(buffer, 0);
                    var ofn = new OpenFileName();
                    ofn.lStructSize = Marshal.SizeOf(typeof(OpenFileName));
                    ofn.hwndOwner = owner;
                    ofn.lpstrFilter = "VRM avatar (*.vrm)\0*.vrm\0All files (*.*)\0*.*\0\0";
                    ofn.nFilterIndex = 1;
                    ofn.lpstrFile = buffer;
                    ofn.nMaxFile = max;
                    ofn.lpstrInitialDir = initialDir;
                    ofn.lpstrTitle = title;
                    ofn.lpstrDefExt = "vrm";
                    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_HIDEREADONLY;
                    if (GetOpenFileNameW(ref ofn))
                        result = Marshal.PtrToStringUni(buffer);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                finally
                {
                    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                    done = true;
                }
            });
            thread.IsBackground = true;
            try { thread.SetApartmentState(ApartmentState.STA); } catch { }
            thread.Start();
        }

        /// <summary>True once the dialog closed. path is null if the user cancelled.</summary>
        public bool TryGetResult(out string path, out string err)
        {
            path = null;
            err = null;
            if (!IsOpen || !done) return false;
            IsOpen = false;
            path = string.IsNullOrEmpty(result) ? null : result;
            err = error;
            return true;
        }
    }
}
