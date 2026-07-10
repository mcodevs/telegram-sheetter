using Microsoft.Win32;

namespace MultiCardSync.App.Services;

/// <summary>Ilovani OS startup'iga qo'shish/olib tashlash.</summary>
public interface IStartupRegistrar
{
    bool IsSupported { get; }
    bool IsRegistered();
    void Register();
    void Unregister();
}

/// <summary>
/// Windows: HKCU\...\Run kalitiga yozadi (admin huquqi shart emas).
/// Boshqa OS (macOS dev) da — no-op.
/// </summary>
public sealed class WindowsStartupRegistrar : IStartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MultiCardSync";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is not null;
    }

    public void Register()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return;

        // --tray: kompyuter yoqilganda oyna ochilmasin, to'g'ridan-to'g'ri tray'ga.
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\" --tray");
    }

    public void Unregister()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
