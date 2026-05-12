using Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Client
{
    public class CsvParser
    {
        private const int MaxValidRows = 100;
        private readonly string csvFilePath;
        private readonly string rejectLogPath;

        public CsvParser(string csvFilePath, string rejectLogPath)
        {
            this.csvFilePath = csvFilePath;
            this.rejectLogPath = rejectLogPath;
        }

        public List<SensorSample> LoadSamples()
        {
            List<SensorSample> validSamples = new List<SensorSample>();

            using (CsvReaderWrapper reader = new CsvReaderWrapper(csvFilePath))
            using (StreamWriterWrapper rejectLog = new StreamWriterWrapper(rejectLogPath, append: false))
            {
                if (reader.EndOfStream)
                {
                    Console.WriteLine("[CLIENT] CSV fajl je prazan.");
                    return validSamples;
                }

                string headerLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(headerLine))
                {
                    Console.WriteLine("[CLIENT] CSV fajl nema zaglavlje.");
                    return validSamples;
                }

                rejectLog.WriteLine($"REJECT LOG - generisan: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                rejectLog.WriteLine("---------------------------------------------------");

                int[] columnIndices = ResolveColumnIndices(headerLine);

                if (columnIndices == null)
                {
                    string msg = "Nije moguce pronadji sve neophodne kolone u zaglavlju CSV fajla.";
                    Console.WriteLine($"[CLIENT] {msg}");
                    rejectLog.WriteLine($"[GRESKA ZAGLAVLJA] {msg}");
                    rejectLog.WriteLine($"Zaglavlje: {headerLine}");
                    return validSamples;
                }

                int lineNumber = 1;
                int validCount = 0;

                while (!reader.EndOfStream)
                {
                    lineNumber++;
                    string line = reader.ReadLine();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (validCount >= MaxValidRows)
                    {
                        rejectLog.WriteLine($"[Red {lineNumber}] VISAN LIMIT ({MaxValidRows} validnih redova) - red preskocen: {line}");
                        continue;
                    }

                    SensorSample sample = TryParseLine(line, columnIndices, lineNumber, rejectLog);

                    if (sample != null)
                    {
                        validSamples.Add(sample);
                        validCount++;
                    }
                }

                rejectLog.WriteLine("---------------------------------------------------");
                rejectLog.WriteLine($"Ukupno ucitano: {validCount} validnih uzoraka.");
            }

            return validSamples;
        }

        private int[] ResolveColumnIndices(string headerLine)
        {
            string[] headers = headerLine.Split(',');

            int idxVolume    = FindColumnIndex(headers, new[] { "volume", "vol", "sound" });
            int idxTempDHT   = FindColumnIndex(headers, new[] { "temperaturedht", "tempdht", "dht" });
            int idxTempBMP   = FindColumnIndex(headers, new[] { "temperaturebmp", "tempbmp", "bmp" });
            int idxPressure  = FindColumnIndex(headers, new[] { "pressure", "press" });
            int idxDateTime  = FindColumnIndex(headers, new[] { "datetime", "date" });

            if (idxVolume < 0 || idxTempDHT < 0 || idxTempBMP < 0 || idxPressure < 0 || idxDateTime < 0)
            {
                Console.WriteLine($"[CLIENT] Pronadjene kolone: Volume={idxVolume}, T_DHT={idxTempDHT}, T_BMP={idxTempBMP}, Pressure={idxPressure}, DateTime={idxDateTime}");
                return null;
            }

            return new int[] { idxVolume, idxTempDHT, idxTempBMP, idxPressure, idxDateTime };
        }

        private int FindColumnIndex(string[] headers, string[] candidates)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                string normalized = NormalizeHeader(headers[i]);
                foreach (string candidate in candidates)
                {
                    if (normalized == candidate || normalized.Contains(candidate))
                        return i;
                }
            }
            return -1;
        }

        private string NormalizeHeader(string header)
        {
            int bracketPos = header.IndexOf('[');
            if (bracketPos > 0)
                header = header.Substring(0, bracketPos);

            return header.Trim()
                         .ToLowerInvariant()
                         .Replace("\"", "")
                         .Replace("-", "")
                         .Replace("_", "")
                         .Replace(" ", "");
        }

        private SensorSample TryParseLine(string line, int[] columnIndices, int lineNumber, StreamWriterWrapper rejectLog)
        {
            string[] fields = line.Split(',');

            int maxIndex = Math.Max(Math.Max(columnIndices[0], columnIndices[1]),
                           Math.Max(Math.Max(columnIndices[2], columnIndices[3]), columnIndices[4]));

            if (fields.Length <= maxIndex)
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEDOVOLJNO KOLONA ({fields.Length}): {line}");
                return null;
            }

            string rawVolume = fields[columnIndices[0]].Trim();
            string rawTempDHT = fields[columnIndices[1]].Trim();
            string rawTempBMP = fields[columnIndices[2]].Trim();
            string rawPressure = fields[columnIndices[3]].Trim();
            string rawDateTime = fields[columnIndices[4]].Trim();

            double volume, tempDHT, tempBMP, pressure;
            DateTime dateTime;

            if (!TryParseDouble(rawVolume, out volume))
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEVAZECI FORMAT Volume='{rawVolume}': {line}");
                return null;
            }

            if (!TryParseDouble(rawTempDHT, out tempDHT))
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEVAZECI FORMAT T_DHT='{rawTempDHT}': {line}");
                return null;
            }

            if (!TryParseDouble(rawTempBMP, out tempBMP))
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEVAZECI FORMAT T_BMP='{rawTempBMP}': {line}");
                return null;
            }

            if (!TryParseDouble(rawPressure, out pressure))
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEVAZECI FORMAT Pressure='{rawPressure}': {line}");
                return null;
            }

            if (!TryParseDateTime(rawDateTime, out dateTime))
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEVAZECI FORMAT DateTime='{rawDateTime}': {line}");
                return null;
            }

            if (pressure <= 0)
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEVAZECA VREDNOST Pressure={pressure} mora biti > 0: {line}");
                return null;
            }

            if (volume < 0)
            {
                rejectLog.WriteLine($"[Red {lineNumber}] NEVAZECA VREDNOST Volume={volume} mora biti >= 0: {line}");
                return null;
            }

            return new SensorSample(volume, tempDHT, tempBMP, pressure, dateTime);
        }

        private bool TryParseDouble(string raw, out double result)
        {
            raw = raw.Replace("\"", "");
            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }

        private bool TryParseDateTime(string raw, out DateTime result)
        {
            raw = raw.Replace("\"", "");

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            long unixSeconds;
            if (long.TryParse(raw, out unixSeconds))
            {
                result = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds).ToLocalTime();
                return true;
            }

            string[] formats = new[]
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm",
                "dd/MM/yyyy HH:mm:ss",
                "dd.MM.yyyy HH:mm:ss",
                "MM/dd/yyyy HH:mm:ss"
            };

            return DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        public static SessionMeta BuildSessionMeta(List<SensorSample> samples)
        {
            if (samples == null || samples.Count == 0)
                return null;

            SensorSample first = samples[0];
            return new SessionMeta(first.Volume, first.TemperatureDHT, first.TemperatureBMP, first.Pressure, first.DateTime);
        }
    }
}
