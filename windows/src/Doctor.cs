using System;
using System.Globalization;

namespace Parrot.Windows
{
    internal static class Doctor
    {
        internal static int Run()
        {
            bool modelReady = false;
            bool microphoneReady = false;
            Console.WriteLine("Parrot for Windows doctor");
            Console.WriteLine("Recording: not started");
            Console.WriteLine("Network transcription: not used");
            Console.WriteLine("Audio/transcript file persistence: disabled");
            Console.WriteLine("Expected sherpa-onnx runtime: " + ParakeetRecognizer.RuntimeVersion);
            Console.WriteLine("Model: " + ParakeetRecognizer.ModelId);

            ParakeetRecognizer recognizer = new ParakeetRecognizer();
            try
            {
                double loadSeconds = recognizer.Initialize();
                modelReady = true;
                Console.WriteLine(
                    "PASS local model: initialized and warmed in "
                    + loadSeconds.ToString("0.000", CultureInfo.InvariantCulture)
                    + " seconds");
            }
            catch (Exception exception)
            {
                Console.WriteLine("BLOCKED local model: " + SafeMessage(exception));
            }
            finally
            {
                try { recognizer.Dispose(); }
                catch (Exception exception)
                {
                    modelReady = false;
                    Console.WriteLine("BLOCKED model cleanup: " + SafeMessage(exception));
                }
            }

            try
            {
                WaveInCaptureSession.ProbeDefaultMicrophone();
                microphoneReady = true;
                Console.WriteLine("PASS default microphone: initialized without recording");
            }
            catch (Exception exception)
            {
                Console.WriteLine("BLOCKED default microphone: " + SafeMessage(exception));
            }

            Console.WriteLine(
                "Quality note: local CPU transcription uses the bundled Parakeet TDT 0.6B v2 int8 model.");
            Console.WriteLine(
                "Doctor initialization does not prove that a live speech sample can be captured.");
            return modelReady && microphoneReady ? 0 : 2;
        }

        private static string SafeMessage(Exception exception)
        {
            return String.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
        }
    }
}
