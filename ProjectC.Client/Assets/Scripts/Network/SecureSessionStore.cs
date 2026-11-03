using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ProjectC.Client
{
    // Windows Credential Manager atomically stores the entire rotated token pair.
    // Tokens never enter PlayerPrefs, deck files, or Unity's serialized assets.
    internal interface ISessionStore
    {
        string Read();
        void Write(string value);
        void Delete();
    }

    internal sealed class SecureSessionStore : ISessionStore
    {
        private const uint GenericCredential = 1;
        private const uint PersistLocalMachine = 2;
        private const int NotFound = 1168;
        private const int MaximumCredentialBytes = 2560;
        private readonly string _target;

        public SecureSessionStore(string apiUrl)
        {
            _target = "ProjectC.Session/" + apiUrl;
        }

        public string Read()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!CredRead(_target, GenericCredential, 0, out var pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NotFound)
                {
                    return null;
                }
                throw new Win32Exception(error);
            }
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                var bytes = new byte[credential.BlobSize];
                Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CredFree(pointer);
            }
#else
            throw new PlatformNotSupportedException("이 빌드에는 OS 보안 세션 저장소가 필요합니다. 현재 Windows를 지원합니다.");
#endif
        }

        public void Write(string value)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > MaximumCredentialBytes)
            {
                throw new InvalidOperationException("Session exceeds credential storage capacity.");
            }
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new Credential
                {
                    Type = GenericCredential,
                    TargetName = _target,
                    BlobSize = (uint)bytes.Length,
                    Blob = blob,
                    Persist = PersistLocalMachine,
                    UserName = "ProjectC",
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
                Marshal.FreeHGlobal(blob);
            }
#else
            throw new PlatformNotSupportedException("OS secure session storage is unavailable.");
#endif
        }

        public void Delete()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!CredDelete(_target, GenericCredential, 0) && Marshal.GetLastWin32Error() != NotFound)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
#else
            throw new PlatformNotSupportedException("OS secure session storage is unavailable.");
#endif
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags, Type;
            public string TargetName, Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint BlobSize;
            public IntPtr Blob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias, UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);
    }
}
