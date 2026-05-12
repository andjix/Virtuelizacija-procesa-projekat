using Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.ServiceModel;

namespace Client
{
    class Program
    {
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
            Console.Write("Odabir: ");
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

            Console.WriteLine($"[CLIENT] Ucitano {samples.Count} validnih uzoraka. Pocinje slanje...");

            SessionMeta meta = CsvParser.BuildSessionMeta(samples);

            ExecuteSessionWithDisposeGuard(meta, samples);
        }

        static void ExecuteSessionWithDisposeGuard(SessionMeta meta, List<SensorSample> samples)
        {
            CsvReaderWrapper demoResource = null;

            try
            {
                demoResource = new CsvReaderWrapper(ConfigurationManager.AppSettings["csvPath"]);

                OperationResponse startResponse = proxy.StartSession(meta);
                Console.WriteLine($"[CLIENT] StartSession -> {startResponse}");

                if (!startResponse.IsAcknowledged)
                {
                    Console.WriteLine("[CLIENT] Servis nije prihvatio pokretanje sesije.");
                    return;
                }

                int sentCount = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    OperationResponse pushResponse = proxy.PushSample(samples[i]);
                    Console.WriteLine($"[CLIENT] PushSample [{i + 1}/{samples.Count}] -> {pushResponse}");

                    if (!pushResponse.IsAcknowledged)
                    {
                        Console.WriteLine($"[CLIENT] Servis nije prihvatio uzorak {i + 1}. Prekidam slanje.");
                        break;
                    }
                    sentCount++;
                }

                Console.WriteLine($"[CLIENT] Poslato {sentCount} od {samples.Count} uzoraka.");

                OperationResponse endResponse = proxy.EndSession();
                Console.WriteLine($"[CLIENT] EndSession -> {endResponse}");
            }
            catch (FaultException<DataFormatFault> e)
            {
                Console.WriteLine($"[CLIENT] DataFormatFault: [{e.Detail.FieldName}] {e.Detail.Message}");
                SafeEndSession();
            }
            catch (FaultException<ValidationFault> e)
            {
                Console.WriteLine($"[CLIENT] ValidationFault: [{e.Detail.FieldName}] {e.Detail.Message} (Ocekivano: {e.Detail.ExpectedRange})");
                SafeEndSession();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[CLIENT] Neocekivana greska u toku prenosa: {e.Message}");
                Console.WriteLine("[CLIENT] Dispose pattern garantuje oslobadjanje resursa cak i pri izuzetku.");
                SafeEndSession();
            }
            finally
            {
                if (demoResource != null)
                {
                    demoResource.Dispose();
                    Console.WriteLine("[CLIENT] [Dispose] Resursi su uspesno oslobodjeni u finally bloku.");
                }
            }
        }

        static void SafeEndSession()
        {
            try
            {
                proxy.EndSession();
                Console.WriteLine("[CLIENT] Sesija zatvorena nakon greske.");
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
                Console.WriteLine($"[CLIENT] Greska pri EndSession: {e.Message}");
            }
        }

    }
}
