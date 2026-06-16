using Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.ServiceModel;
using System.Threading;

namespace Client
{
    class Program
    {
        private const int SampleDelayMs = 250;

        static IKancelarijskiSenzorService proxy;
        static ChannelFactory<IKancelarijskiSenzorService> factory;

        static void Main(string[] args)
        {
            factory = new ChannelFactory<IKancelarijskiSenzorService>("KancelarijskiSenzorService");
            proxy = factory.CreateChannel();

            bool running = true;
            while (running)
            {
                PrintMenu();
                string choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        SendDataFromCsv();
                        break;
                    case "2":
                        EndActiveSession();
                        break;
                    case "0":
                        running = false;
                        break;
                    default:
                        Console.WriteLine("[CLIENT] Nepostojeca opcija.");
                        break;
                }
            }

            factory.Close();
            Console.WriteLine("[CLIENT] Program zatvoren.");
        }

        static void PrintMenu()
        {
            Console.WriteLine();
            Console.WriteLine("=========================================");
            Console.WriteLine("  Kancelarijski Senzor - Klijent");
            Console.WriteLine("=========================================");
            Console.WriteLine("1. Ucitaj CSV i posalji podatke servisu");
            Console.WriteLine("2. Zavrsi aktivnu sesiju");
            Console.WriteLine("0. Izlaz");
            Console.Write("Izbor: ");
        }

        static void SendDataFromCsv()
        {
            string csvPath = ConfigurationManager.AppSettings["csvPath"];
            string rejectLogPath = ConfigurationManager.AppSettings["rejectLogPath"];

            if (string.IsNullOrWhiteSpace(csvPath))
            {
                Console.WriteLine("[CLIENT] Putanja do CSV fajla nije podesena u App.config (kljuc: csvPath).");
                return;
            }

            if (string.IsNullOrWhiteSpace(rejectLogPath))
            {
                rejectLogPath = "rejects.log";
            }

            Console.WriteLine($"[CLIENT] Ucitavam CSV: {csvPath}");
            Console.WriteLine($"[CLIENT] Odbaceni redovi ce biti upisani u: {rejectLogPath}");

            CsvParser parser = new CsvParser(csvPath, rejectLogPath);
            List<SensorSample> samples;

            try
            {
                samples = parser.LoadSamples();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[CLIENT] Greska pri citanju CSV-a: {e.Message}");
                return;
            }

            if (samples.Count == 0)
            {
                Console.WriteLine("[CLIENT] Nema validnih uzoraka za slanje.");
                return;
            }

            WriteColoredLine($"[INFO] Ucitano {samples.Count} validnih uzoraka. Pocinje slanje...", ConsoleColor.Gray);

            SessionMeta meta = CsvParser.BuildSessionMeta(samples);

            ExecuteSessionWithDisposeGuard(meta, samples);
        }

        static void ExecuteSessionWithDisposeGuard(SessionMeta meta, List<SensorSample> samples)
        {
            string sessionLogPath = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            StreamWriterWrapper sessionLog = null;

            try
            {
                sessionLog = new StreamWriterWrapper(sessionLogPath, append: false);
                sessionLog.WriteLine($"[SESIJA POKRENUTA] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sessionLog.WriteLine($"Meta: {meta}");

                OperationResponse startResponse = proxy.StartSession(meta);
                WriteColoredLine($"[INFO] StartSession -> {startResponse}", ConsoleColor.Gray);
                sessionLog.WriteLine($"StartSession: {startResponse}");

                if (!startResponse.IsAcknowledged)
                {
                    Console.WriteLine("[CLIENT] Servis nije prihvatio pokretanje sesije.");
                    sessionLog.WriteLine("[SESIJA ODBIJENA] Servis nije prihvatio StartSession.");
                    return;
                }

                int sentCount = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    OperationResponse pushResponse = proxy.PushSample(samples[i]);
                    sessionLog.WriteLine($"PushSample [{i + 1}/{samples.Count}]: {pushResponse}");

                    if (!pushResponse.IsAcknowledged)
                    {
                        WriteColoredLine($"{i + 1} -> Uzorak odbijen: {pushResponse.Message}", ConsoleColor.Red);
                        WriteColoredLine($"[INFO] Servis nije prihvatio uzorak {i + 1}. Prekidam slanje.", ConsoleColor.Gray);
                        sessionLog.WriteLine($"[PREKID] Uzorak {i + 1} odbijen.");
                        break;
                    }

                    WriteColoredLine($"{i + 1} -> Uzorak uspesno obradjen: {pushResponse.Message}", ConsoleColor.Green);
                    sentCount++;
                    Thread.Sleep(SampleDelayMs);
                }

                WriteColoredLine($"[INFO] Poslato {sentCount} od {samples.Count} uzoraka.", ConsoleColor.Gray);

                OperationResponse endResponse = proxy.EndSession();
                WriteColoredLine($"[INFO] EndSession -> {endResponse}", ConsoleColor.Gray);
                sessionLog.WriteLine($"EndSession: {endResponse}");
                sessionLog.WriteLine($"[SESIJA ZAVRSENA] Poslato {sentCount}/{samples.Count} uzoraka.");
            }
            catch (FaultException<DataFormatFault> e)
            {
                WriteColoredLine($"[ERROR] DataFormatFault: [{e.Detail.FieldName}] {e.Detail.Message}", ConsoleColor.Red);
                sessionLog?.WriteLine($"[GRESKA DataFormatFault] [{e.Detail.FieldName}] {e.Detail.Message}");
                SafeEndSession();
            }
            catch (FaultException<ValidationFault> e)
            {
                WriteColoredLine($"[ERROR] ValidationFault: [{e.Detail.FieldName}] {e.Detail.Message} (Ocekivano: {e.Detail.ExpectedRange})", ConsoleColor.Red);
                sessionLog?.WriteLine($"[GRESKA ValidationFault] [{e.Detail.FieldName}] {e.Detail.Message} (Ocekivano: {e.Detail.ExpectedRange})");
                SafeEndSession();
            }
            catch (Exception e)
            {
                WriteColoredLine($"[ERROR] Neocekivana greska u toku prenosa: {e.Message}", ConsoleColor.Red);
                sessionLog?.WriteLine($"[GRESKA] Neocekivana greska usred prenosa: {e.Message}");
                SafeEndSession();
            }
            finally
            {
                if (sessionLog != null)
                {
                    sessionLog.Dispose();
                    Console.WriteLine($"[CLIENT] [Dispose] Session log '{sessionLogPath}' uspesno zatvoren u finally bloku.");
                }
            }
        }

        static void SafeEndSession()
        {
            try
            {
                proxy.EndSession();
                WriteColoredLine("[INFO] Sesija zatvorena nakon greske.", ConsoleColor.Gray);
            }
            catch
            {
            }
        }

        static void EndActiveSession()
        {
            try
            {
                OperationResponse response = proxy.EndSession();
                Console.WriteLine($"[CLIENT] EndSession -> {response}");
            }
            catch (Exception e)
            {
                WriteColoredLine($"[ERROR] Greska pri EndSession: {e.Message}", ConsoleColor.Red);
            }
        }

        static void WriteColoredLine(string message, ConsoleColor color)
        {
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }

    }
}
