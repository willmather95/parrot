using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Parrot for Windows")]
[assembly: AssemblyDescription("Local Windows toggle-to-dictate preview")]
[assembly: AssemblyCompany("Parrot")]
[assembly: AssemblyProduct("Parrot for Windows")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace Parrot.Windows
{
    internal static class Program
    {
        internal const string Version = "preview 0.1.0";

        [STAThread]
        public static int Main(string[] arguments)
        {
            if (HasArgument(arguments, "--version"))
            {
                ConsoleSupport.AttachToParent();
                Console.WriteLine("Parrot for Windows " + Version);
                return 0;
            }

            if (HasArgument(arguments, "--self-test"))
            {
                ConsoleSupport.AttachToParent();
                return SelfTests.Run();
            }

            if (HasArgument(arguments, "--doctor"))
            {
                ConsoleSupport.AttachToParent();
                return Doctor.Run();
            }

            if (arguments != null && arguments.Length > 0)
            {
                ConsoleSupport.AttachToParent();
                Console.Error.WriteLine("Unknown argument. Use --version, --doctor, or --self-test.");
                return 64;
            }

            bool createdNew = false;
            Mutex instanceMutex = null;
            try
            {
                instanceMutex = new Mutex(true, @"Local\Parrot.Windows.SingleInstance", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Parrot for Windows is already running. Open it from the notification area.",
                        "Parrot for Windows",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return 3;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            finally
            {
                if (instanceMutex != null)
                {
                    if (createdNew)
                    {
                        try
                        {
                            instanceMutex.ReleaseMutex();
                        }
                        catch (ApplicationException)
                        {
                        }
                    }

                    instanceMutex.Dispose();
                }
            }
        }

        private static bool HasArgument(string[] arguments, string expected)
        {
            return arguments != null
                && arguments.Length == 1
                && String.Equals(arguments[0], expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class ConsoleSupport
    {
        internal static void AttachToParent()
        {
            if (!NativeMethods.AttachConsole(NativeMethods.AttachParentProcess))
            {
                return;
            }

            try
            {
                StreamWriter output = new StreamWriter(Console.OpenStandardOutput());
                output.AutoFlush = true;
                Console.SetOut(output);
                StreamWriter error = new StreamWriter(Console.OpenStandardError());
                error.AutoFlush = true;
                Console.SetError(error);
            }
            catch (IOException)
            {
            }
        }
    }
}
