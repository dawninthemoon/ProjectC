#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ProjectC.Client
{
    // Login Keychain generic passwords. No security CLI, temporary files or token arguments.
    internal sealed class MacKeychainSessionStore : ISessionStore
    {
        internal const string Service = "ProjectC.Unity.Session";
        private const int Success = 0;
        private const int ItemNotFound = -25300;
        private const int MaximumSessionBytes = 2560;
        private readonly string _account;

        internal MacKeychainSessionStore(string account)
        {
            _account = account;
        }

        private IntPtr Query()
        {
            var query = Native.Dictionary();
            try
            {
                Native.CFDictionarySetValue(query, Native.Key("kSecClass"), Native.Key("kSecClassGenericPassword"));
                SetString(query, "kSecAttrService", Service);
                SetString(query, "kSecAttrAccount", _account);
                Native.CFDictionarySetValue(query, Native.Key("kSecAttrSynchronizable"), Native.False);
                return query;
            }
            catch
            {
                Native.CFRelease(query);
                throw;
            }
        }

        private static void SetString(IntPtr dictionary, string key, string value)
        {
            var text = Native.String(value);
            try
            {
                Native.CFDictionarySetValue(dictionary, Native.Key(key), text);
            }
            finally
            {
                Native.CFRelease(text);
            }
        }

        internal static void RequireSuccess(int status, string operation)
        {
            if (status != Success)
            {
                throw new SecureSessionStoreException("macOS Keychain", operation, status);
            }
        }

        public string Read()
        {
            var query = Query();
            IntPtr result = IntPtr.Zero;
            byte[] bytes = null;
            try
            {
                Native.CFDictionarySetValue(query, Native.Key("kSecReturnData"), Native.True);
                Native.CFDictionarySetValue(query, Native.Key("kSecMatchLimit"), Native.Key("kSecMatchLimitOne"));
                int status = Native.SecItemCopyMatching(query, out result);
                // Only an absent item permits guest creation. Denial/locked Keychain is an error.
                if (status == ItemNotFound)
                {
                    return null;
                }
                RequireSuccess(status, "조회");
                if (result == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Keychain returned no session data.");
                }
                long length = Native.CFDataGetLength(result).ToInt64();
                if (length < 0 || length > MaximumSessionBytes)
                {
                    throw new InvalidOperationException("Stored Keychain session exceeds the supported capacity.");
                }
                bytes = new byte[(int)length];
                if (bytes.Length > 0)
                {
                    Marshal.Copy(Native.CFDataGetBytePtr(result), bytes, 0, bytes.Length);
                }
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            finally
            {
                if (bytes != null)
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }
                if (result != IntPtr.Zero)
                {
                    Native.CFRelease(result);
                }
                Native.CFRelease(query);
            }
        }

        public void Write(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            IntPtr query = IntPtr.Zero;
            IntPtr attributes = IntPtr.Zero;
            IntPtr data = IntPtr.Zero;
            try
            {
                if (bytes.Length > MaximumSessionBytes)
                {
                    throw new InvalidOperationException("Session exceeds credential storage capacity.");
                }
                query = Query();
                attributes = Native.Dictionary();
                data = Native.CFDataCreate(IntPtr.Zero, bytes, new IntPtr(bytes.Length));
                if (data == IntPtr.Zero)
                {
                    throw new OutOfMemoryException("Cannot allocate Keychain session data.");
                }
                Native.CFDictionarySetValue(attributes, Native.Key("kSecValueData"), data);
                int status = Native.SecItemUpdate(query, attributes);
                if (status == ItemNotFound)
                {
                    Native.CFDictionarySetValue(query, Native.Key("kSecValueData"), data);
                    // A concurrent insertion is reported, never blindly overwritten.
                    status = Native.SecItemAdd(query, IntPtr.Zero);
                }
                RequireSuccess(status, "저장");
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
                if (data != IntPtr.Zero)
                {
                    Native.CFRelease(data);
                }
                if (attributes != IntPtr.Zero)
                {
                    Native.CFRelease(attributes);
                }
                if (query != IntPtr.Zero)
                {
                    Native.CFRelease(query);
                }
            }
        }

        public void Delete()
        {
            var query = Query();
            try
            {
                int status = Native.SecItemDelete(query);
                if (status != ItemNotFound)
                {
                    RequireSuccess(status, "삭제");
                }
            }
            finally
            {
                Native.CFRelease(query);
            }
        }

        private static class Native
        {
            private const string Security = "/System/Library/Frameworks/Security.framework/Security";
            private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
            private const int LazyBinding = 1;
            private const uint Utf8 = 0x08000100;
            private static readonly IntPtr SecurityHandle = Open(Security);
            private static readonly IntPtr CoreHandle = Open(CoreFoundation);
            internal static readonly IntPtr True = Marshal.ReadIntPtr(Symbol(CoreHandle, "kCFBooleanTrue"));
            internal static readonly IntPtr False = Marshal.ReadIntPtr(Symbol(CoreHandle, "kCFBooleanFalse"));
            private static readonly IntPtr KeyCallbacks = Symbol(CoreHandle, "kCFTypeDictionaryKeyCallBacks");
            private static readonly IntPtr ValueCallbacks = Symbol(CoreHandle, "kCFTypeDictionaryValueCallBacks");

            private static IntPtr Open(string path)
            {
                var handle = dlopen(path, LazyBinding);
                if (handle == IntPtr.Zero)
                {
                    throw new PlatformNotSupportedException("macOS security frameworks are unavailable.");
                }
                return handle;
            }

            private static IntPtr Symbol(IntPtr library, string name)
            {
                var symbol = dlsym(library, name);
                if (symbol == IntPtr.Zero)
                {
                    throw new PlatformNotSupportedException("A required macOS security symbol is unavailable: " + name);
                }
                return symbol;
            }

            internal static IntPtr Key(string name) => Marshal.ReadIntPtr(Symbol(SecurityHandle, name));
            internal static IntPtr Dictionary()
            {
                var dictionary = CFDictionaryCreateMutable(IntPtr.Zero, IntPtr.Zero, KeyCallbacks, ValueCallbacks);
                if (dictionary == IntPtr.Zero)
                {
                    throw new OutOfMemoryException("Cannot allocate Keychain query.");
                }
                return dictionary;
            }

            internal static IntPtr String(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                var text = CFStringCreateWithBytes(IntPtr.Zero, bytes, new IntPtr(bytes.Length), Utf8, false);
                if (text == IntPtr.Zero)
                {
                    throw new OutOfMemoryException("Cannot allocate Keychain identifier.");
                }
                return text;
            }

            [DllImport(SystemLibrary)] private static extern IntPtr dlopen(string path, int mode);
            [DllImport(SystemLibrary)] private static extern IntPtr dlsym(IntPtr handle, string name);
            [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, IntPtr capacity, IntPtr keys, IntPtr values);
            [DllImport(CoreFoundation)] internal static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
            [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithBytes(IntPtr allocator, byte[] bytes, IntPtr length, uint encoding, [MarshalAs(UnmanagedType.I1)] bool external);
            [DllImport(CoreFoundation)] internal static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, IntPtr length);
            [DllImport(CoreFoundation)] internal static extern IntPtr CFDataGetLength(IntPtr data);
            [DllImport(CoreFoundation)] internal static extern IntPtr CFDataGetBytePtr(IntPtr data);
            [DllImport(CoreFoundation)] internal static extern void CFRelease(IntPtr value);
            [DllImport(Security)] internal static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
            [DllImport(Security)] internal static extern int SecItemUpdate(IntPtr query, IntPtr attributes);
            [DllImport(Security)] internal static extern int SecItemAdd(IntPtr attributes, IntPtr result);
            [DllImport(Security)] internal static extern int SecItemDelete(IntPtr query);
        }
    }
}
#endif
