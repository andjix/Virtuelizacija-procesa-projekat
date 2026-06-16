using Common;
using System;
using System.Configuration;
using System.Globalization;
using System.ServiceModel;

namespace Server
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class KancelarijskiSenzorService : IKancelarijskiSenzorService
    {
        public delegate void TransferStartedHandler(SessionMeta meta);
        public delegate void SampleReceivedHandler(SensorSample sample);
        public delegate void TransferCompletedHandler(int numberOfSamples);
        public delegate void WarningRaisedHandler(string warningType, string message);

        public event TransferStartedHandler OnTransferStarted;
        public event SampleReceivedHandler OnSampleReceived;
        public event TransferCompletedHandler OnTransferCompleted;
        public event WarningRaisedHandler OnWarningRaised;

        private bool sessionActive = false;
        private SessionMeta currentSessionMeta = null;

        private int sampleCount = 0;
        private double volumeSum = 0.0;
        private double volumeMean = 0.0;
        private double vThreshold = 10.0;
        private double tDhtThreshold = 2.0;
        private double tBmpThreshold = 2.0;
        private double outOfBandPercent = 25.0;
        private SensorSample previousSample = null;
        private StreamWriterWrapper measurementsWriter = null;
        private StreamWriterWrapper rejectsWriter = null;

        public KancelarijskiSenzorService()
        {
            OnTransferStarted += LogTransferStarted;
            OnSampleReceived += LogSampleReceived;
            OnTransferCompleted += LogTransferCompleted;
            OnWarningRaised += LogWarningRaised;
        }

        public OperationResponse StartSession(SessionMeta meta)
        {
            ValidateSessionMeta(meta);

            CloseSessionFiles();
            PrepareSessionFiles();

            sessionActive = true;
            currentSessionMeta = meta;

            sampleCount = 0;
            volumeSum = 0.0;
            volumeMean = 0.0;
            previousSample = null;
            vThreshold = double.TryParse(
                ConfigurationManager.AppSettings["V_threshold"],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double vTh) ? vTh : 10.0;
            tDhtThreshold = double.TryParse(
                ConfigurationManager.AppSettings["T_dht_threshold"],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double tDht) ? tDht : 2.0;
            tBmpThreshold = double.TryParse(
                ConfigurationManager.AppSettings["T_bmp_threshold"],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double tBmp) ? tBmp : 2.0;
            outOfBandPercent = double.TryParse(
                ConfigurationManager.AppSettings["OutOfBand_Percent"],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double pct) ? pct : 25.0;

            OnTransferStarted?.Invoke(meta);
            WriteColoredLine($"[INFO] Sesija pokrenuta. {meta}", ConsoleColor.Gray);

            return OperationResponse.Ack("IN_PROGRESS", "Sesija uspesno pokrenuta.");
        }

        public OperationResponse PushSample(SensorSample sample)
        {
            if (!sessionActive)
            {
                return OperationResponse.Nack("FAILED", "Nema aktivne sesije. Pozovite StartSession pre slanja uzoraka.");
            }

            WriteColoredLine("\n[INFO] prenos u toku...", ConsoleColor.Gray);
            OnSampleReceived?.Invoke(sample);
            WriteColoredLine("\n[INFO] završen prenos",ConsoleColor.Gray);

            try
            {
                ValidateSample(sample);
            }
            catch (FaultException<DataFormatFault> e)
            {
                WriteRejectedSample(sample, e.Detail.Message);
                throw;
            }
            catch (FaultException<ValidationFault> e)
            {
                WriteRejectedSample(sample, e.Detail.Message);
                throw;
            }

            sampleCount++;
            volumeSum += sample.Volume;
            volumeMean = volumeSum / sampleCount;

            WriteAcceptedSample(sample);
            AnalyzeVolume(sample);
            AnalyzeTemperature(sample);
            previousSample = sample;

            WriteColoredLine($"[SAMPLE] Uzorak primljen: {sample}", ConsoleColor.Green);

            return OperationResponse.Ack("IN_PROGRESS", "Uzorak uspesno primljen.");
        }

        public OperationResponse EndSession()
        {
            if (!sessionActive)
            {
                return OperationResponse.Nack("FAILED", "Nema aktivne sesije.");
            }

            sessionActive = false;
            currentSessionMeta = null;
            CloseSessionFiles();

            OnTransferCompleted?.Invoke(sampleCount);
            WriteColoredLine("[INFO] zavrsen prenos", ConsoleColor.Gray);
            WriteColoredLine("[INFO] Sesija zavrsena.", ConsoleColor.Gray);

            return OperationResponse.Ack("COMPLETED", "Sesija uspesno zavrsena.");
        }

        private void ValidateSessionMeta(SessionMeta meta)
        {
            if (meta == null)
            {
                throw new FaultException<DataFormatFault>(
                    new DataFormatFault("Meta zaglavlje sesije ne sme biti null.", "meta"),
                    new FaultReason("Nevazece meta zaglavlje."));
            }

            if (meta.DateTime == default(DateTime))
            {
                throw new FaultException<DataFormatFault>(
                    new DataFormatFault("DateTime polje nije postavljeno.", "DateTime"),
                    new FaultReason("Nevazeci datum i vreme u meta zaglavlju."));
            }

            if (meta.Pressure <= 0 || meta.Pressure > 1100)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Pritisak mora biti u opsegu 0 do 1100 hPa.", "Pressure", "0 < Pressure <= 1100"),
                    new FaultReason("Nevazeca vrednost pritiska u meta zaglavlju."));
            }

            if (meta.Volume < 0)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Jacina zvuka ne moze biti negativna.", "Volume", ">= 0"),
                    new FaultReason("Nevazeca vrednost jacine zvuka u meta zaglavlju."));
            }

            if (meta.TemperatureDHT < -40 || meta.TemperatureDHT > 80)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Temperatura DHT senzora van dozvoljenog opsega.", "TemperatureDHT", "-40 do 80 °C"),
                    new FaultReason("Nevazeca vrednost temperature DHT u meta zaglavlju."));
            }

            if (meta.TemperatureBMP < -40 || meta.TemperatureBMP > 85)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Temperatura BMP senzora van dozvoljenog opsega.", "TemperatureBMP", "-40 do 85 °C"),
                    new FaultReason("Nevazeca vrednost temperature BMP u meta zaglavlju."));
            }
        }

        private void ValidateSample(SensorSample sample)
        {
            if (sample == null)
            {
                throw new FaultException<DataFormatFault>(
                    new DataFormatFault("Uzorak ne sme biti null.", "sample"),
                    new FaultReason("Nevazeci uzorak."));
            }

            if (sample.DateTime == default(DateTime))
            {
                throw new FaultException<DataFormatFault>(
                    new DataFormatFault("DateTime polje uzorka nije postavljeno.", "DateTime"),
                    new FaultReason("Nevazeci datum i vreme u uzorku."));
            }

            if (sample.Pressure <= 0 || sample.Pressure > 1100)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Pritisak mora biti u opsegu 0 do 1100 hPa.", "Pressure", "0 < Pressure <= 1100"),
                    new FaultReason("Nevazeca vrednost pritiska u uzorku."));
            }

            if (sample.Volume < 0)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Jacina zvuka ne moze biti negativna.", "Volume", ">= 0"),
                    new FaultReason("Nevazeca vrednost jacine zvuka u uzorku."));
            }

            if (sample.TemperatureDHT < -40 || sample.TemperatureDHT > 80)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Temperatura DHT senzora van dozvoljenog opsega.", "TemperatureDHT", "-40 do 80 °C"),
                    new FaultReason("Nevazeca vrednost temperature DHT u uzorku."));
            }

            if (sample.TemperatureBMP < -40 || sample.TemperatureBMP > 85)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault("Temperatura BMP senzora van dozvoljenog opsega.", "TemperatureBMP", "-40 do 85 °C"),
                    new FaultReason("Nevazeca vrednost temperature BMP u uzorku."));
            }
        }

        private void AnalyzeVolume(SensorSample sample)
        {
            if (previousSample != null)
            {
                double deltaV = sample.Volume - previousSample.Volume;

                if (Math.Abs(deltaV) > vThreshold)
                {
                    string direction = deltaV > 0 ? "iznad o\u010dekivanog" : "ispod o\u010dekivanog";
                    string message = $"Volume={sample.Volume:F2}, prethodni Volume={previousSample.Volume:F2}, deltaV={deltaV:F2}, smer={direction}";
                    RaiseWarning("VolumeSpike", message);
                }
            }

            double lower = volumeMean * (1.0 - outOfBandPercent / 100.0);
            double upper = volumeMean * (1.0 + outOfBandPercent / 100.0);

            if (sample.Volume < lower)
            {
                string message = $"Volume={sample.Volume:F2}, Vmean={volumeMean:F2}, donja granica={lower:F2}, smer=ispod o\u010dekivane vrednosti";
                RaiseWarning("OutOfBandWarning", message);
            }
            else if (sample.Volume > upper)
            {
                string message = $"Volume={sample.Volume:F2}, Vmean={volumeMean:F2}, gornja granica={upper:F2}, smer=iznad o\u010dekivane vrednosti";
                RaiseWarning("OutOfBandWarning", message);
            }
        }

        private void AnalyzeTemperature(SensorSample sample)
        {
            if (previousSample == null)
            {
                return;
            }

            double deltaDht = sample.TemperatureDHT - previousSample.TemperatureDHT;

            if (Math.Abs(deltaDht) > tDhtThreshold)
            {
                string direction = deltaDht > 0 ? "iznad o\u010dekivanog" : "ispod o\u010dekivanog";
                string message = $"tip=TemperatureSpikeDHT, TemperatureDHT={sample.TemperatureDHT:F2}, prethodni TemperatureDHT={previousSample.TemperatureDHT:F2}, deltaDht={deltaDht:F2}, smer={direction}";
                RaiseWarning("TemperatureSpikeDHT", message);
            }

            double deltaBmp = sample.TemperatureBMP - previousSample.TemperatureBMP;

            if (Math.Abs(deltaBmp) > tBmpThreshold)
            {
                string direction = deltaBmp > 0 ? "iznad o\u010dekivanog" : "ispod o\u010dekivanog";
                string message = $"tip=TemperatureSpikeBMP, TemperatureBMP={sample.TemperatureBMP:F2}, prethodni TemperatureBMP={previousSample.TemperatureBMP:F2}, deltaBmp={deltaBmp:F2}, smer={direction}";
                RaiseWarning("TemperatureSpikeBMP", message);
            }
        }

        private void LogTransferStarted(SessionMeta meta)
        {
            WriteColoredLine($"[INFO] OnTransferStarted: {meta}", ConsoleColor.Gray);
        }

        private void LogSampleReceived(SensorSample sample)
        {
            string sampleText = sample == null ? "null" : sample.ToString();
            WriteColoredLine($"[SAMPLE] OnSampleReceived: {sampleText}", ConsoleColor.Green);
        }

        private void LogTransferCompleted(int numberOfSamples)
        {
            WriteColoredLine($"[INFO] OnTransferCompleted: primljeno {numberOfSamples} uzoraka.", ConsoleColor.Gray);
        }

        private void LogWarningRaised(string warningType, string message)
        {
            WriteColoredLine($"[WARNING] {warningType}: {message}", ConsoleColor.Red);
        }

        private void RaiseWarning(string warningType, string message)
        {
            OnWarningRaised?.Invoke(warningType, message);
        }

        private void WriteColoredLine(string message, ConsoleColor color)
        {
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }

        private void PrepareSessionFiles()
        {
            measurementsWriter = new StreamWriterWrapper("measurements_session.csv", append: false);
            rejectsWriter = new StreamWriterWrapper("rejects.csv", append: false);

            measurementsWriter.WriteLine("DateTime,Volume,TemperatureDHT,TemperatureBMP,Pressure");
            rejectsWriter.WriteLine("RejectedAt,Reason,DateTime,Volume,TemperatureDHT,TemperatureBMP,Pressure");

            measurementsWriter.Flush();
            rejectsWriter.Flush();
        }

        private void WriteAcceptedSample(SensorSample sample)
        {
            measurementsWriter?.WriteLine(FormatSampleForCsv(sample));
            measurementsWriter?.Flush();
        }

        private void WriteRejectedSample(SensorSample sample, string reason)
        {
            string rejectedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            rejectsWriter?.WriteLine($"{rejectedAt},{EscapeCsv(reason)},{FormatSampleForCsv(sample)}");
            rejectsWriter?.Flush();
        }

        private string FormatSampleForCsv(SensorSample sample)
        {
            if (sample == null)
            {
                return ",,,,";
            }

            return string.Join(",",
                sample.DateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                sample.Volume.ToString(CultureInfo.InvariantCulture),
                sample.TemperatureDHT.ToString(CultureInfo.InvariantCulture),
                sample.TemperatureBMP.ToString(CultureInfo.InvariantCulture),
                sample.Pressure.ToString(CultureInfo.InvariantCulture));
        }

        private string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private void CloseSessionFiles()
        {
            if (measurementsWriter != null)
            {
                measurementsWriter.Dispose();
                measurementsWriter = null;
            }

            if (rejectsWriter != null)
            {
                rejectsWriter.Dispose();
                rejectsWriter = null;
            }
        }
    }
}
