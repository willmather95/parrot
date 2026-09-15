using System;
using System.Collections.Generic;
using System.Speech.Recognition;

namespace Parrot.Windows
{
    internal static class Doctor
    {
        internal static int Run()
        {
            bool engineReady = false;
            bool microphoneInitialized = false;

            Console.WriteLine("Parrot for Windows doctor");
            Console.WriteLine("Recording: not started");
            Console.WriteLine("Network transcription: not used");
            Console.WriteLine("Audio/transcript file persistence: disabled");

            RecognizerInfo selected = null;
            try
            {
                IList<RecognizerInfo> installed = SpeechRecognitionEngine.InstalledRecognizers();
                Console.WriteLine("Installed speech engines: " + installed.Count);
                selected = SpeechEngineFactory.FindEnglishRecognizer();
                if (selected == null)
                {
                    Console.WriteLine("BLOCKED speech engine: no installed English recognizer");
                }
                else
                {
                    Console.WriteLine(
                        "PASS speech engine: " + selected.Name + " [" + selected.Culture.Name + "]");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("BLOCKED speech engine: " + exception.Message);
            }

            SpeechRecognitionEngine engine = null;
            if (selected != null)
            {
                try
                {
                    engine = new SpeechRecognitionEngine(selected);
                    engine.LoadGrammar(new DictationGrammar());
                    engineReady = true;
                    Console.WriteLine("PASS dictation engine: initialized");
                }
                catch (Exception exception)
                {
                    Console.WriteLine("BLOCKED dictation engine: " + exception.Message);
                }
            }

            if (engine != null && engineReady)
            {
                try
                {
                    engine.SetInputToDefaultAudioDevice();
                    microphoneInitialized = true;
                    Console.WriteLine("PASS default microphone: input initialized without recording");
                }
                catch (Exception exception)
                {
                    Console.WriteLine("BLOCKED default microphone: " + exception.Message);
                }
            }

            if (engine != null)
            {
                engine.Dispose();
            }

            Console.WriteLine(
                "Quality note: this preview uses the installed Windows English dictation engine. "
                + "Accuracy varies by Windows installation and microphone.");
            Console.WriteLine(
                "Doctor initialization does not prove that a live speech sample can be captured.");
            return engineReady && microphoneInitialized ? 0 : 2;
        }
    }
}
