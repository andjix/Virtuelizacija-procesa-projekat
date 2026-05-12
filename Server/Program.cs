using System;
using System.ServiceModel;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            ServiceHost host = new ServiceHost(typeof(KancelarijskiSenzorService));

            try
            {
                host.Open();
                Console.WriteLine("[SERVER] Servis je pokrenut. Pritisnite bilo koji taster za zatvaranje.");
                Console.ReadKey();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[SERVER] Greska pri pokretanju servisa: {e.Message}");
            }
            finally
            {
                host.Close();
                Console.WriteLine("[SERVER] Servis je zatvoren.");
            }
        }
    }
}
