using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OwaWidget.App.Services;

public static class CredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public static string TargetFor(string server)
    {
        return $"OwaWidget:{new Uri(server).Host}";
    }

    public static void Save(string server, string user, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var blobHandle = Marshal.AllocHGlobal(blob.Length);

        try
        {
            Marshal.Copy(blob, 0, blobHandle, blob.Length);

            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Marshal.StringToCoTaskMemUni(TargetFor(server)),
                UserName = Marshal.StringToCoTaskMemUni(user),
                CredentialBlob = blobHandle,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine
            };

            try
            {
                if (!CredWrite(ref credential, 0))
                {
                    throw new IOException($"CredWrite failed with error {Marshal.GetLastWin32Error()}.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(credential.TargetName);
                Marshal.FreeCoTaskMem(credential.UserName);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobHandle);
        }
    }

    public static (string User, string Password)? TryLoad(string server)
    {
        if (!CredRead(TargetFor(server), CredTypeGeneric, 0, out var handle))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);

            var user = credential.UserName != IntPtr.Zero
                ? Marshal.PtrToStringUni(credential.UserName) ?? string.Empty
                : string.Empty;

            var password = credential.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? string.Empty
                : string.Empty;

            return (user, password);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public static void Delete(string server)
    {
        CredDelete(TargetFor(server), CredTypeGeneric, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}