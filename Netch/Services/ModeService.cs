using Netch.Controllers;
using Netch.Interfaces;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Utils;

namespace Netch.Services;

public class ModeService
{
    public static readonly ModeService Instance = new();

    public string ModeDirectoryFullName => Path.Combine(Global.NetchDir, "mode");

    public string GetRelativePath(string fullName)
    {
        var root = Path.GetFullPath(ModeDirectoryFullName);
        var path = Path.GetFullPath(fullName);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mode file is outside the mode directory", nameof(fullName));

        return Path.GetRelativePath(root, path);
    }

    public string GetFullPath(string relativeName)
    {
        var root = Path.GetFullPath(ModeDirectoryFullName);
        var path = Path.GetFullPath(Path.Combine(root, relativeName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mode path must stay inside the mode directory", nameof(relativeName));

        return path;
    }

    public bool IsCustomMode(Mode mode)
    {
        if (string.IsNullOrWhiteSpace(mode.FullName))
            return false;

        var path = Path.GetFullPath(mode.FullName);
        var customRoot = Path.GetFullPath(Path.Combine(ModeDirectoryFullName, "Custom"));
        return path.StartsWith(customRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public void Load()
    {
        Global.Modes.Clear();
        LoadCore(ModeDirectoryFullName);
        Sort();
        Global.MainForm.LoadModes();
    }

    private void LoadCore(string modeDirectory)
    {
        foreach (var directory in Directory.GetDirectories(modeDirectory))
            LoadCore(directory);

        // skip Directory with a disabled file in
        if (File.Exists(Path.Combine(modeDirectory, Constants.DisableModeDirectoryFileName)))
            return;

        foreach (var file in Directory.GetFiles(modeDirectory))
        {
            try
            {
                Global.Modes.Add(ModeHelper.LoadMode(file));
            }
            catch (NotSupportedException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Log.Warning(e, "Load mode \"{FileName}\" failed", file);
            }
        }
    }

    private static void SortCollection()
    {
        static int BuiltInPriority(Mode mode)
        {
            if (Instance.IsCustomMode(mode))
                return int.MaxValue;

            return Path.GetFileName(mode.FullName) switch
            {
                "Global.json" => 0,
                "绕过中国大陆 IP 代理.txt" => 1,
                "仅 Chrome 代理.json" => 2,
                "eFootball.json" => 3,
                "暗黑破坏神4.json" => 4,
                "战地6.json" => 5,
                _ => int.MaxValue
            };
        }

        Global.Modes.Sort((a, b) =>
        {
            var priority = BuiltInPriority(a).CompareTo(BuiltInPriority(b));
            return priority != 0 ? priority : string.Compare(a.i18NRemark, b.i18NRemark, StringComparison.Ordinal);
        });
    }

    public void Add(Mode mode)
    {
        if (mode.FullName == null)
            throw new InvalidOperationException();

        Global.Modes.Add(mode);
        Sort();

        Directory.CreateDirectory(Path.GetDirectoryName(mode.FullName)!);
        mode.WriteFile();
    }

    public void Sort()
    {
        SortCollection();
        Global.MainForm.LoadModes();
    }

    public static void Delete(Mode mode)
    {
        if (mode.FullName == null)
            throw new ArgumentException(nameof(mode.FullName));

        if (!Instance.IsCustomMode(mode))
            throw new InvalidOperationException("Only custom modes can be deleted. Built-in modes are protected.");

        Global.MainForm.ModeComboBox.Items.Remove(mode);
        Global.Modes.Remove(mode);

        if (File.Exists(mode.FullName))
            File.Delete(mode.FullName);
    }

    public static IModeController GetModeControllerByType(ModeType type, out ushort? port, out string portName)
    {
        port = null;
        portName = string.Empty;
        switch (type)
        {
            case ModeType.ProcessMode:
                return new NFController();
            case ModeType.TunMode:
                return new TUNController();
            case ModeType.ShareMode:
                return new PcapController();
            default:
                Log.Error("Unknown Mode Type \"{Type}\"", (int)type);
                throw new MessageException("Unknown Mode Type");
        }
    }
}
