using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Parrot for Windows")]
[assembly: AssemblyDescription("Local Windows toggle-to-dictate preview with Parakeet")]
[assembly: AssemblyCompany("Parrot")]
[assembly: AssemblyProduct("Parrot for Windows")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

namespace Parrot.Windows
{
    internal sealed class TranscribeFileOptions
    {
        internal string InputPath;
        internal string OutputPath;

        internal static bool TryParse(string[] arguments, out TranscribeFileOptions options)
        {
            options = null;
            if (arguments == null || arguments.Length != 4)
            {
                return false;
            }

            string input = null;
            string output = null;
            for (int index = 0; index < arguments.Length; index += 2)
            {
                string name = arguments[index];
                string value = arguments[index + 1];
                if (String.IsNullOrWhiteSpace(value))
                {
                    return false;
                }
                if (String.Equals(name, "--transcribe-file", StringComparison.OrdinalIgnoreCase)
                    && input == null)
                {
                    input = value;
                }
                else if (String.Equals(name, "--output-json", StringComparison.OrdinalIgnoreCase)
                    && output == null)
                {
                    output = value;
                }
                else
                {
                    return false;
                }
            }

            if (input == null || output == null)
            {
                return false;
            }
            try
            {
                options = new TranscribeFileOptions();
                options.InputPath = Path.GetFullPath(input);
                options.OutputPath = Path.GetFullPath(output);
            }
            catch (Exception exception)
            {
                if (exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is PathTooLongException)
                {
                    options = null;
                    return false;
                }
                throw;
            }
            return !String.Equals(
                options.InputPath,
                options.OutputPath,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class BenchmarkJson
    {
        internal static string Create(
            string text,
            double audioSeconds,
            double loadSeconds,
            double decodeSeconds,
            int chunkCount,
            long peakWorkingSetBytes)
        {
            return "{"
                + "\"text\":\"" + Escape(text) + "\","
                + "\"audio_seconds\":" + Number(audioSeconds) + ","
                + "\"load_seconds\":" + Number(loadSeconds) + ","
                + "\"decode_seconds\":" + Number(decodeSeconds) + ","
                + "\"model\":\"" + Escape(ParakeetRecognizer.ModelId) + "\","
                + "\"model_revision\":\"" + Escape(ParakeetRecognizer.ModelRevision) + "\","
                + "\"runtime_version\":\"" + Escape(ParakeetRecognizer.RuntimeVersion) + "\","
                + "\"chunk_count\":" + chunkCount.ToString(CultureInfo.InvariantCulture) + ","
                + "\"peak_working_set_bytes\":"
                + peakWorkingSetBytes.ToString(CultureInfo.InvariantCulture)
                + "}";
        }

        internal static string Escape(string value)
        {
            if (value == null) { return String.Empty; }
            StringBuilder builder = new StringBuilder(value.Length + 16);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            return builder.ToString();
        }

        private static string Number(double value)
        {
            return value.ToString("0.000000", CultureInfo.InvariantCulture);
        }
    }

    internal static class Program
    {
        internal const string Version = "preview 0.2.0";

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

            TranscribeFileOptions options;
            if (TranscribeFileOptions.TryParse(arguments, out options))
            {
                ConsoleSupport.AttachToParent();
                return RunTranscribeFile(options);
            }
            if (arguments != null && arguments.Length > 0)
            {
                ConsoleSupport.AttachToParent();
                Console.Error.WriteLine(
                    "Usage: Parrot.exe [--version|--doctor|--self-test] "
                    + "or --transcribe-file <16k-mono-pcm16.wav> --output-json <path>");
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
                        try { instanceMutex.ReleaseMutex(); }
                        catch (ApplicationException) { }
                    }
                    instanceMutex.Dispose();
                }
            }
        }

        private static int RunTranscribeFile(TranscribeFileOptions options)
        {
            WavAudio audio;
            try
            {
                audio = WavFile.Read(options.InputPath);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Invalid WAV input: " + SafeMessage(exception));
                return 3;
            }

            ParakeetRecognizer recognizer = new ParakeetRecognizer();
            try
            {
                double loadSeconds = recognizer.Initialize();
                ParakeetTranscriptionResult result = recognizer.Transcribe(
                    audio.Samples,
                    audio.SampleRate,
                    null);
                long peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64;
                string json = BenchmarkJson.Create(
                    result.Text,
                    audio.DurationSeconds,
                    loadSeconds,
                    result.DecodeSeconds,
                    result.ChunkCount,
                    peakWorkingSet);
                File.WriteAllText(options.OutputPath, json, new UTF8Encoding(false));
                Console.WriteLine("Transcription metrics written to the requested JSON file.");
                return 0;
            }
            catch (FileNotFoundException exception)
            {
                Console.Error.WriteLine("Local inference package incomplete: " + SafeMessage(exception));
                return 2;
            }
            catch (DllNotFoundException exception)
            {
                Console.Error.WriteLine("Local inference runtime unavailable: " + SafeMessage(exception));
                return 2;
            }
            catch (BadImageFormatException exception)
            {
                Console.Error.WriteLine("Local inference runtime is incompatible: " + SafeMessage(exception));
                return 2;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Local transcription failed: " + SafeMessage(exception));
                return 4;
            }
            finally
            {
                try { recognizer.Dispose(); }
                catch (Exception) { }
            }
        }

        private static bool HasArgument(string[] arguments, string expected)
        {
            return arguments != null
                && arguments.Length == 1
                && String.Equals(arguments[0], expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeMessage(Exception exception)
        {
            return String.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
        }
    }

    internal static class ConsoleSupport
    {
        internal static void AttachToParent()
        {
            if (!NativeMethods.AttachConsole(NativeMethods.AttachParentProcess)) { return; }
            try
            {
                StreamWriter output = new StreamWriter(Console.OpenStandardOutput());
                output.AutoFlush = true;
                Console.SetOut(output);
                StreamWriter error = new StreamWriter(Console.OpenStandardError());
                error.AutoFlush = true;
                Console.SetError(error);
            }
            catch (IOException) { }
        }
    }
}
