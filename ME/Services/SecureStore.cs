using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ME.Services
{
    /// <summary>
    /// 使用 Windows DPAPI（CryptProtectData）对敏感设置（如 DeepSeek API Key）做本机加密存储。
    /// 密文只在本机当前用户下可解密，避免明文落盘。
    /// </summary>
    public static class SecureStore
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = Encoding.UTF8.GetBytes(plain);
            var inBlob = new DATA_BLOB { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(bytes.Length) };
            Marshal.Copy(bytes, 0, inBlob.pbData, bytes.Length);
            var outBlob = new DATA_BLOB();
            try
            {
                if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var outBytes = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, outBytes, 0, outBlob.cbData);
                return Convert.ToBase64String(outBytes);
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        public static string Decrypt(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return "";
            try
            {
                var bytes = Convert.FromBase64String(encrypted);
                var inBlob = new DATA_BLOB { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(bytes.Length) };
                Marshal.Copy(bytes, 0, inBlob.pbData, bytes.Length);
                var outBlob = new DATA_BLOB();
                try
                {
                    if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                        return "";
                    var outBytes = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, outBytes, 0, outBlob.cbData);
                    return Encoding.UTF8.GetString(outBytes);
                }
                finally
                {
                    if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                    if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
