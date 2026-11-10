using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

internal static class OneClickBootstrap
{
    [STAThread]
    private static int Main(string[] args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "DgmCourseBootstrap-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string archive = Path.Combine(temp, "package.zip");
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("CourseStudioPackage.zip"))
            {
                if (resource == null) throw new InvalidOperationException("The Course Studio package is missing from setup.");
                using (Stream output = File.Create(archive)) resource.CopyTo(output);
            }
            string extracted = Path.Combine(temp, "package");
            ZipFile.ExtractToDirectory(archive, extracted);
            string setup = Path.Combine(extracted, "Setup-CourseStudio.ps1");
            string command = Path.Combine(extracted, "Setup-CourseStudio.cmd");
            if (!File.Exists(setup) || !File.Exists(command) || !File.Exists(Path.Combine(extracted, "package-manifest.json")))
                throw new InvalidOperationException("The embedded Course Studio package is incomplete.");
            if (args.Length == 1 && args[0] == "--check") return 0;
            string commandShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = commandShell,
                Arguments = "/d /c \"\"" + command + "\"\"",
                UseShellExecute = true,
                WorkingDirectory = extracted
            }))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Setup did not complete. Check the message in the setup window, close Disc Golf Masters, and try again.");
            }
            MessageBox.Show("Course Studio and the map loader are installed. Start the game once and open its offline course selector, then use the Course Studio desktop shortcut.", "Course Studio setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length != 1 || args[0] != "--check") MessageBox.Show(ex.Message, "Course Studio setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(temp);
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("DgmCourseBootstrap-", StringComparison.OrdinalIgnoreCase))
            {
                try { if (Directory.Exists(full)) Directory.Delete(full, true); }
                catch { }
            }
        }
    }
}
