using System.Runtime.InteropServices;

namespace Mostik.Mobile.Services;

internal static class VoskNative
{
    private const string Library = "vosk";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void vosk_set_log_level(int level);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint vosk_model_new([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void vosk_model_free(nint model);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint vosk_recognizer_new(nint model, float rate);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int vosk_recognizer_accept_waveform(nint recognizer, byte[] bytes, int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint vosk_recognizer_result(nint recognizer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint vosk_recognizer_partial_result(nint recognizer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint vosk_recognizer_final_result(nint recognizer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void vosk_recognizer_free(nint recognizer);
    internal static string Copy(nint value) => Marshal.PtrToStringUTF8(value) ?? "{}";
}
