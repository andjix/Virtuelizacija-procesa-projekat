using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class OperationResponse
    {
        private bool isAcknowledged;
        private string status;
        private string message;

        public OperationResponse(bool isAcknowledged, string status, string message)
        {
            this.isAcknowledged = isAcknowledged;
            this.status = status;
            this.message = message;
        }

        [DataMember]
        public bool IsAcknowledged { get => isAcknowledged; set => isAcknowledged = value; }

        [DataMember]
        public string Status { get => status; set => status = value; }

        [DataMember]
        public string Message { get => message; set => message = value; }

        public static OperationResponse Ack(string status, string message = "")
        {
            return new OperationResponse(true, status, message);
        }

        public static OperationResponse Nack(string status, string message = "")
        {
            return new OperationResponse(false, status, message);
        }

        public override string ToString()
        {
            string ackLabel = isAcknowledged ? "ACK" : "NACK";
            return $"[{ackLabel}] Status={Status} | {Message}";
        }
    }
}
