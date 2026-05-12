using System;
using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class SessionMeta
    {
        private double volume;
        private double temperatureDHT;
        private double temperatureBMP;
        private double pressure;
        private DateTime dateTime;

        public SessionMeta(double volume, double temperatureDHT, double temperatureBMP, double pressure, DateTime dateTime)
        {
            this.volume = volume;
            this.temperatureDHT = temperatureDHT;
            this.temperatureBMP = temperatureBMP;
            this.pressure = pressure;
            this.dateTime = dateTime;
        }

        [DataMember]
        public double Volume { get => volume; set => volume = value; }

        [DataMember]
        public double TemperatureDHT { get => temperatureDHT; set => temperatureDHT = value; }

        [DataMember]
        public double TemperatureBMP { get => temperatureBMP; set => temperatureBMP = value; }

        [DataMember]
        public double Pressure { get => pressure; set => pressure = value; }

        [DataMember]
        public DateTime DateTime { get => dateTime; set => dateTime = value; }

        public override string ToString()
        {
            return $"SessionMeta [{DateTime:yyyy-MM-dd HH:mm:ss}] Vol={Volume} T_DHT={TemperatureDHT} T_BMP={TemperatureBMP} P={Pressure}";
        }
    }
}
